/**
 * VedaBaseModern 2 - High-Performance WebView2 Reader Engine
 * Handles rendering, fluid scrolling, native DOM find highlighting,
 * user text highlighting, keyboard shortcuts forwarding, and theme synchronization.
 */

(function () {
    const contentEl = document.getElementById('content');
    const toolbarEl = document.getElementById('selection-toolbar');

    let currentMatches = [];
    let activeMatchIndex = -1;
    let currentSelectionInfo = null;

    // ---- Host Messaging Bridge ----
    function notifyHost(message) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(message);
        } else {
            console.log('[Reader -> Host]', message);
        }
    }

    window.onerror = function (msg, url, line, col, err) {
        notifyHost({ action: 'js_error', error: String(msg) + ' at ' + line + ':' + col + (err ? ' ' + err.stack : '') });
    };

    // Expose API on window for C# ExecuteScriptAsync calls
    window.reader = {
        renderVerse: renderVerse,
        renderChapter: renderChapter,
        scrollToVerse: scrollToVerse,
        findSearch: findSearch,
        setActiveFindMatch: setActiveFindMatch,
        clearFindSearch: clearFindSearch,
        setTheme: setTheme,
        setTextBrightness: setTextBrightness,
        setFontSizes: setFontSizes,
        setLineHeight: setLineHeight,
        setContentMaxWidth: setContentMaxWidth,
        attachHighlightId: attachHighlightId,
        onNavigateScripture: onNavigateScripture,
        openNoteEditor: openNoteEditor,
        closeNoteEditor: closeNoteEditor,
        editNote: editNote,
        saveNote: saveNote,
        deleteNote: deleteNote,
        onNoteSaved: onNoteSaved,
        onNoteDeleted: onNoteDeleted,
        exportSingleNote: exportSingleNote,
        exportDraft: exportDraft,
        formatBlock: formatBlock,
        execFormat: execFormat,
        execHighlight: execHighlight,
        insertTable: insertTable,
        toggleTableDialog: toggleTableDialog,
        closeTableDialog: closeTableDialog,
        onCellHover: onCellHover,
        onCellClick: onCellClick,
        onGridMouseLeave: onGridMouseLeave,
        onTableInputChanged: onTableInputChanged,
        insertCustomTable: insertCustomTable,
        insertPresetTable: insertPresetTable,
        promptLink: promptLink,
        toggleLinkDialog: toggleLinkDialog,
        closeLinkDialog: closeLinkDialog,
        insertCustomLink: insertCustomLink,
        insertCode: insertCode,
        handleEditorInput: handleEditorInput,
        insertAtPrompt: insertAtPrompt,
        setZenMode: setZenMode,
        onHashtagClick: onHashtagClick,
        onWikiLinkClick: onWikiLinkClick,
        showLexiconCard: showLexiconCard,
        hideLexiconCard: hideLexiconCard,
        toggleChantingGuide: toggleChantingGuide,
        toggleChantingPulse: toggleChantingPulse,
        stopChantingPulse: stopChantingPulse,
        highlightSearchTerms: highlightSearchTerms,
        nextHit: nextHit,
        prevHit: prevHit,
        clearSearchHits: clearSearchHits
    };

    // Notify C# host that the web engine is initialized and ready
    document.addEventListener('DOMContentLoaded', () => {
        notifyHost({ action: 'ready' });
    });

    // ---- Rendering & Highlight Helpers ----

    function escapeHtml(text) {
        if (!text) return '';
        return text
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function groupHighlightsByField(highlights, targetRecordKey) {
        const map = {};
        if (!highlights || !Array.isArray(highlights)) return map;
        for (const h of highlights) {
            const hRecordKey = h.recordKey || h.RecordKey;
            if (targetRecordKey && hRecordKey && hRecordKey !== targetRecordKey) continue;
            const fieldVal = h.field || h.Field || 'Purport';
            const f = fieldVal.toLowerCase();
            if (!map[f]) map[f] = [];
            map[f].push(h);
        }
        return map;
    }

    function applyHighlights(rawText, fieldHighlights) {
        if (!rawText) return '';
        let escaped = escapeHtml(rawText);
        if (!fieldHighlights || fieldHighlights.length === 0) return escaped;

        for (const h of fieldHighlights) {
            const selectedText = h.selectedText || h.SelectedText;
            if (!selectedText || selectedText.trim().length === 0) continue;
            const targetEscaped = escapeHtml(selectedText);
            const rawColor = h.color !== undefined ? h.color : h.Color;
            let colorStr = 'yellow';
            if (typeof rawColor === 'number') {
                colorStr = rawColor === 1 ? 'green' : (rawColor === 2 ? 'blue' : 'yellow');
            } else if (typeof rawColor === 'string') {
                colorStr = rawColor.toLowerCase();
            }
            const id = h.id || h.Id || '';
            const markTag = `<mark class="hl-mark-${colorStr}" data-highlight-id="${escapeHtml(id)}">${targetEscaped}</mark>`;
            escaped = escaped.split(targetEscaped).join(markTag);
        }
        return escaped;
    }

    function attachHighlightId(id, text) {
        if (!id) return;
        const marks = contentEl.querySelectorAll('mark:not([data-highlight-id]), mark[data-highlight-id=""]');
        for (const m of marks) {
            if (!text || m.textContent === text) {
                m.dataset.highlightId = id;
                break;
            }
        }
    }

    function formatSynonyms(synonymsText, fieldHighlights) {
        if (!synonymsText) return '';
        const parts = synonymsText.split(';');
        return parts.map(part => {
            const trimmed = part.trim();
            if (!trimmed) return '';
            const dashIdx = trimmed.indexOf('—');
            if (dashIdx > 0) {
                const lemma = trimmed.substring(0, dashIdx).trim();
                const gloss = trimmed.substring(dashIdx).trim();
                const lemmaHtml = `<span class="sanskrit-word">${escapeHtml(lemma)}</span>`;
                const glossHtml = applyHighlights(gloss, fieldHighlights);
                return `${lemmaHtml}${glossHtml}`;
            }
            return applyHighlights(trimmed, fieldHighlights);
        }).filter(p => p.length > 0).join('; ') + (parts.length > 1 ? '.' : '');
    }

    function onNavigateScripture(event, ref) {
        if (event) {
            event.preventDefault();
            event.stopPropagation();
        }
        notifyHost({
            action: 'navigate_reference',
            reference: ref
        });
    }

    function linkifyScriptureReferences(html) {
        if (!html) return '';
        const parts = html.split(/(<[^>]+>)/g);
        for (let i = 0; i < parts.length; i += 2) {
            let text = parts[i];
            if (!text) continue;

            // 1. Bhagavad-gita: "@bg 1.1", "Bhagavad-gītā (4.9)", "Bg. 4.9"
            text = text.replace(/(@)?(?:Bhagavad[- ]g[īi]t[āa]|Bg\.?)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,2}(?:[-–—]\d{1,2})?)\s*\)?)/gi, (m, at, ch, vs) => {
                const baseVs = vs.split(/[-–—]/)[0];
                const targetRef = `BG ${ch}.${baseVs}`;
                const atClass = at ? ' at-mention' : '';
                return `<a class="scripture-link${atClass}" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>`;
            });

            // 2. Srimad-Bhagavatam: "@sb 3.4.5", "Śrīmad-Bhāgavatam (1.2.6)", "SB 5.6.6"
            text = text.replace(/(@)?(?:[ŚS]r[īi]mad[- ]Bh[āa]gavatam|SB|S\.B\.)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,2})[\.:](\d{1,2}(?:[-–—]\d{1,2})?)\s*\)?)/gi, (m, at, canto, ch, vs) => {
                const baseVs = vs.split(/[-–—]/)[0];
                const targetRef = `SB ${canto}.${ch}.${baseVs}`;
                const atClass = at ? ' at-mention' : '';
                return `<a class="scripture-link${atClass}" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>`;
            });

            // 3. Caitanya-caritamrta: "@cc madhya 2.6", "Cc. Madhya 22.83"
            text = text.replace(/(@)?(?:Caitanya[- ]carit[āa]m[ṛr]ta|Cc\.?|C\.c\.)\s*(?:(?:[- ]l[īi]l[āa])?\s*)?(?:\(?\s*([ĀA]di|Madhya|Antya)\s*(\d{1,2})[\.:](\d{1,2}(?:[-–—]\d{1,2})?)\s*\)?)/gi, (m, at, lil, ch, vs) => {
                const baseVs = vs.split(/[-–—]/)[0];
                const lilNorm = lil.toLowerCase();
                const lilKey = lilNorm.includes('ad') ? 'Adi' : (lilNorm.includes('madh') ? 'Madhya' : 'Antya');
                const targetRef = `CC ${lilKey} ${ch}.${baseVs}`;
                const atClass = at ? ' at-mention' : '';
                return `<a class="scripture-link${atClass}" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>`;
            });

            // 4. Sri Isopanisad: "@iso 1", "Śrī Īśopaniṣad (mantra 1)"
            text = text.replace(/(@)?(?:[ŚS]r[īi]\s*[ĪI][śs]opani[ṣs]ad|[ĪI][śs]opani[ṣs]ad|Iso\.?)\s*(?:,\s*)?(?:[Mm]antra\s*)?(?:\(?\s*(\d{1,2})\s*\)?)/gi, (m, at, mantra) => {
                const targetRef = `ISO ${mantra}`;
                const atClass = at ? ' at-mention' : '';
                return `<a class="scripture-link${atClass}" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>`;
            });

            // 5. Brahma-samhita: "@bs 5.38", "Brahma-saṁhitā (5.38)"
            text = text.replace(/(@)?(?:Brahma[- ]sa[ṁm]hit[āa]|Bs\.?)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,2}(?:[-–—]\d{1,2})?)\s*\)?)/gi, (m, at, ch, vs) => {
                const baseVs = vs.split(/[-–—]/)[0];
                const targetRef = `BS ${ch}.${baseVs}`;
                const atClass = at ? ' at-mention' : '';
                return `<a class="scripture-link${atClass}" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>`;
            });

            // 6. Nectar of Instruction: "@noi 1", "NOI 1"
            text = text.replace(/(@)?(?:(?:The\s+)?Nectar of Instruction|Upade[śs][āa]m[ṛr]ta|NOI)\s*(?:,\s*)?(?:(?:[Tt]ext|[Vv]erse)\s*)?(?:\(?\s*(\d{1,2})\s*\)?)/gi, (m, at, vs) => {
                const targetRef = `NOI ${vs}`;
                const atClass = at ? ' at-mention' : '';
                return `<a class="scripture-link${atClass}" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>`;
            });

            // 7. Nectar of Devotion: "@nod 1", "NOD 1"
            text = text.replace(/(@)?(?:(?:The\s+)?Nectar of Devotion|NOD)\s*(?:,\s*)?(?:(?:[Cc]hapter|[Ss]ection)\s*)?(?:\(?\s*(\d{1,2})\s*\)?)/gi, (m, at, ch) => {
                const targetRef = `NOD ${ch}`;
                const atClass = at ? ' at-mention' : '';
                return `<a class="scripture-link${atClass}" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>`;
            });

            // 8. Prabhupada Shlokas: "@sps 10.32", "SPS 10.32"
            text = text.replace(/(@)?(?:SPS|[ŚS]r[īi]la\s+Prabhup[āa]da\s+[ŚS]lokas|Srila\s+Prabhupada\s+Slokas)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,3})\s*\)?)/gi, (m, at, sec, vs) => {
                const targetRef = `SPS ${sec}.${vs}`;
                const atClass = at ? ' at-mention' : '';
                return `<a class="scripture-link${atClass}" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>`;
            });

            // 9. Vedanta-sutra quotes outside BG/SB/CC
            text = text.replace(/(?:Ved[āa]nta[- ]s[ūu]tra)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,2})[\.:](\d{1,2})\s*\)?)/gi, (m, ch, sec, vs) => {
                const key = `${ch}.${sec}.${vs}`;
                const spsMap = { '1.1.1': 'SPS 9.1', '1.1.2': 'SPS 9.2', '1.1.12': 'SPS 9.3' };
                const targetRef = spsMap[key];
                return targetRef ? `<a class="scripture-link" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>` : m;
            });

            // 10. Katha Upanisad quotes
            text = text.replace(/(?:Ka[ṭt]ha\s+Upani[ṣs]ad)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,2})[\.:](\d{1,2})\s*\)?)/gi, (m, ch, sec, vs) => {
                const key = `${ch}.${sec}.${vs}`;
                const spsMap = { '1.2.20': 'SPS 10.29', '1.2.23': 'SPS 10.30', '1.3.14': 'SPS 10.31', '2.2.13': 'SPS 10.32' };
                const targetRef = spsMap[key];
                return targetRef ? `<a class="scripture-link" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>` : m;
            });

            // 11. Svetasvatara Upanisad quotes
            text = text.replace(/(?:[ŚS]vet[āa][śs]vatara\s+Upani[ṣs]ad)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,2})\s*\)?)/gi, (m, ch, vs) => {
                const key = `${ch}.${vs}`;
                const spsMap = { '3.19': 'SPS 10.36', '5.9': 'SPS 10.37', '6.8': 'SPS 10.39', '6.38': 'SPS 10.40' };
                const targetRef = spsMap[key];
                return targetRef ? `<a class="scripture-link" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>` : m;
            });

            // 12. Mundaka Upanisad quotes
            text = text.replace(/(?:Mu[ṇn][ḍd]aka\s+Upani[ṣs]ad)\s*(?:\(?\s*(\d{1,2})(?:[\.:](\d{1,2}))?(?:[\.:](\d{1,2}))?\s*\)?)/gi, (m, a, b, c) => {
                const key = c ? `${a}.${b}.${c}` : (b ? `${a}.${b}` : a);
                const spsMap = { '1.2.12': 'SPS 10.33', '1.3': 'SPS 10.34', '3.1.1': 'SPS 10.35' };
                const targetRef = spsMap[key];
                return targetRef ? `<a class="scripture-link" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>` : m;
            });

            // 13. Bhakti-rasamrta-sindhu quotes
            text = text.replace(/(?:Bhakti[- ]ras[āa]m[ṛr]ta[- ]sindhu|BRS|B\.R\.S\.)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,2})[\.:](\d{1,3}(?:[-–—]\d{1,3})?)\s*\)?)/gi, (m, a, b, c) => {
                const baseC = c.split(/[-–—]/)[0];
                const key = `${a}.${b}.${baseC}`;
                const spsMap = {
                    '1.1.12': 'SPS 12.1', '1.1.11': 'SPS 12.2', '1.1.74': 'SPS 12.3',
                    '1.2.4': 'SPS 12.4', '1.2.39': 'SPS 12.5', '1.2.101': 'SPS 12.6',
                    '1.2.187': 'SPS 12.7', '1.2.234': 'SPS 12.8', '1.2.255': 'SPS 12.9',
                    '1.2.256': 'SPS 12.10', '1.3.35': 'SPS 12.11', '1.4.15': 'SPS 12.12',
                    '3.2.35': 'SPS 12.13'
                };
                const targetRef = spsMap[key];
                return targetRef ? `<a class="scripture-link" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>` : m;
            });

            // 14. Brhan-naradiya Purana quotes
            text = text.replace(/(?:B[ṛr]han[- ]n[āa]rad[īi]ya\s+Pur[āa][ṇn]a)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,2})[\.:](\d{1,3})\s*\)?)/gi, (m, a, b, c) => {
                const key = `${a}.${b}.${c}`;
                const spsMap = { '3.8.126': 'SPS 13.4' };
                const targetRef = spsMap[key];
                return targetRef ? `<a class="scripture-link" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>` : m;
            });

            // 15. Mahabharata Udyoga Parva 71.4
            text = text.replace(/(?:Mah[āa]bh[āa]rata\s+Udyoga\s+Parva)\s*(?:\(?\s*(\d{1,2})[\.:](\d{1,2})\s*\)?)/gi, (m, a, b) => {
                const key = `${a}.${b}`;
                const spsMap = { '71.4': 'SPS 14.5' };
                const targetRef = spsMap[key];
                return targetRef ? `<a class="scripture-link" href="#" data-ref="${targetRef}" onclick="reader.onNavigateScripture(event, '${targetRef}')">${m}</a>` : m;
            });

            parts[i] = text;
        }
        return parts.join('');
    }

    const englishProseWords = new Set([
        'the', 'is', 'in', 'of', 'to', 'for', 'by', 'he', 'his', 'him', 'she', 'her', 'we', 'our',
        'they', 'them', 'their', 'you', 'your', 'says', 'said', 'state', 'states', 'stated',
        'explains', 'explained', 'speaks', 'spoke', 'writes', 'wrote', 'lord', 'who', 'which',
        'that', 'this', 'with', 'from', 'have', 'has', 'had', 'were', 'was', 'been', 'being',
        'will', 'would', 'could', 'should', 'about', 'into', 'when', 'where', 'why', 'how'
    ]);

    function isQuotedVerse(text) {
        const rawLines = text.split(/\r?\n/).map(l => l.trim()).filter(l => l.length > 0);
        if (rawLines.length < 2) return false;
        if (rawLines.some(l => l.length > 85)) return false;

        const words = text.toLowerCase().replace(/[^a-z\u00C0-\u024F\s]/g, ' ').split(/\s+/).filter(w => w.length > 0);
        const englishCount = words.filter(w => englishProseWords.has(w)).length;
        if (englishCount > 0) return false;

        const diacritics = (text.match(/[\u0100-\u024F\u1E00-\u1EFF]/g) || []).length;
        return diacritics >= 2;
    }

    function formatQuotedVerseBlock(paragraphText, fieldHighlights) {
        const rawLines = paragraphText.split(/\r?\n/);
        const lines = rawLines.map(l => l.trim()).filter(l => l.length > 0);
        const renderedLines = [];
        for (const line of lines) {
            const isCitation = /^[\u2014\u2013\-\[]\s*(?:Bhagavad|Bg|Śrīmad|SB|Cc|Caitanya|Iso|Bs|NOI|NOD)/i.test(line);
            let lineHtml = applyHighlights(line, fieldHighlights);
            lineHtml = linkifyScriptureReferences(lineHtml);
            if (isCitation) {
                renderedLines.push(`<div class="purport-verse-citation">${lineHtml}</div>`);
            } else {
                renderedLines.push(`<div class="purport-verse-line">${lineHtml}</div>`);
            }
        }
        return `<div class="purport-verse">${renderedLines.join('')}</div>`;
    }

    const dialogueSpeakerRegex = /^((?:Śrīla\s+Prabhupāda|Prabhupāda|Devotee(?:\s+\d+|\s*\([^\)]+\))?|Guest(?:\s+\d+|\s*\([^\)]+\))?|Disciple(?:\s+\d+|\s*\([^\)]+\))?|Dr\.\s+[A-Za-zāīūṛṅñṭḍṇśṣ]+|Reporter|Student|Indian\s+(?:man|lady|gentleman)|Woman|Man|Boy|Girl|[A-ZŚ][a-zāīūṛṅñṭḍṇśṣ]+(?:\s+[A-ZŚ][a-zāīūṛṅñṭḍṇśṣ]+){0,2}(?:\s*\([^\)]+\))?))\s*:\s*(.*)$/s;
    const speakerIgnoreKeywords = /^(?:Note|Chapter|Text|Verse|Verses|See|Ref|Reference|Definition|Example)$/i;

    function sanitizeProsePurport(purportText, recordTitle, chapterTitle) {
        if (!purportText) return '';
        const rawParas = purportText.split(/\r?\n\s*\r?\n/).map(p => p.trim()).filter(p => p.length > 0);
        if (rawParas.length === 0) return '';

        function normalizeTitle(t) {
            if (!t) return '';
            return t.toLowerCase().replace(/^(?:chapter\s+\d+|canto\s+\d+|\d+[\.:]?)\s*[-–—:]?\s*/i, '').replace(/[^a-z0-9]/g, '');
        }

        const normRecTitle = normalizeTitle(recordTitle);
        const normChapTitle = normalizeTitle(chapterTitle);

        let startIdx = 0;

        // Skip leading paragraphs that merely repeat the chapter or record title/number
        while (startIdx < rawParas.length && startIdx < 3) {
            const p = rawParas[startIdx];
            const normP = normalizeTitle(p);
            const isChapNumOnly = /^(?:chapter\s+(?:one|two|three|four|five|six|seven|eight|nine|ten|\d+)|canto\s+\d+|\d+\.)\s*$/i.test(p);
            const matchesTitle = normP.length > 3 && (normP === normRecTitle || normP === normChapTitle);

            if (isChapNumOnly || matchesTitle) {
                startIdx++;
                continue;
            }
            break;
        }

        const cleanedParas = [];
        for (let i = startIdx; i < rawParas.length; i++) {
            const current = rawParas[i];
            const next = (i + 1 < rawParas.length) ? rawParas[i + 1] : null;

            // Pattern: Section marker e.g. "JSD 5.1: Meditation Through Transcendental Sound"
            // followed by duplicate title "Meditation Through Transcendental Sound"
            const sectionMatch = /^([A-Z]{2,4}\s+\d+(?:\.\d+)?)\s*:\s*(.+)$/i.exec(current);
            if (sectionMatch && next) {
                const secTitle = sectionMatch[2].trim();
                if (normalizeTitle(secTitle) === normalizeTitle(next)) {
                    cleanedParas.push(`### ${current}`);
                    i++; // skip next duplicate paragraph
                    continue;
                }
            }

            // Pattern: Narada Bhakti Sutra repetitiveness e.g. "NBS 54" followed by "SŪTRA 54" followed by "SŪTRA"
            const nbsMatch = /^NBS\s+(\d+)\*?$/i.exec(current);
            if (nbsMatch && next && new RegExp(`^S[ŪU]TRA\\s+${nbsMatch[1]}\\*?$`, 'i').test(next)) {
                // Drop the redundant "NBS <N>" reference prefix; "SŪTRA <N>" will be processed next
                continue;
            }

            const sutraNumMatch = /^S[ŪU]TRA\s+(\d+\*?)$/i.exec(current);
            if (sutraNumMatch) {
                cleanedParas.push(`### SŪTRA ${sutraNumMatch[1]}`);
                if (next && /^S[ŪU]TRA$/i.test(next)) {
                    i++; // skip redundant bare "SŪTRA"
                }
                continue;
            }

            if (/^S[ŪU]TRA$/i.test(current)) {
                if (cleanedParas.length > 0 && cleanedParas[cleanedParas.length - 1].startsWith('### SŪTRA')) {
                    continue; // drop duplicate bare SŪTRA
                }
                cleanedParas.push('### SŪTRA');
                continue;
            }

            // Skip legacy non-unicode font garbled strings (e.g. "@TaAtaAe BaiM( vyaAKyaAsyaAma:")
            if (/^[@$][A-Za-z0-9$()\\:;,"'\s]{4,}$/.test(current) || (current.startsWith('s$') && current.length < 40)) {
                continue;
            }

            // Inline asterisk footnote that ends with "SŪTRA N*"
            if (current.startsWith('\\* Translations and purports of the texts marked with an asterisk')) {
                const fnMatch = /(S[ŪU]TRA\s+\d+\*?)$/i.exec(current);
                if (fnMatch) {
                    cleanedParas.push(`### ${fnMatch[1]}`);
                }
                continue;
            }

            cleanedParas.push(current);
        }

        return cleanedParas.join('\n\n');
    }


    function isSubheadingParagraph(text, isProse) {
        if (!text) return false;
        const trimmed = text.trim();
        if (trimmed.startsWith('### ')) return true;
        if (!isProse) return false;
        if (trimmed.length < 3 || trimmed.length > 85) return false;

        // Must start with a capital letter
        if (!/^[A-Z\u00C0-\u00DE\u0100-\u017E\u1E00-\u1E90]/.test(trimmed)) {
            return false;
        }

        // Must not end in sentence punctuation, dialogue quotes, or closing brackets
        if (/[.?!:;,\[\]\(\)"'”’]$/.test(trimmed)) {
            return false;
        }

        // Must not start with quote, bullet, or digit
        if (/^["'“‘\[\(\*\d]/.test(trimmed)) {
            return false;
        }

        const words = trimmed.split(/\s+/);
        if (words.length < 1 || words.length > 12) {
            return false;
        }

        // Ignore typical sentence starts
        if (/^(?:In the|In\s+[A-Z]|From the|According to|As stated in|The scripture|Regarding|This is|It is|He was|He is|They are|We should|One should|Thus|Then|There|When|While|Because|Therefore|Furthermore|Moreover|Consequently|Similarly|On the other hand|After a few|In other words|For example|By such|Such as|With|And|Or|But|If|So)\b/i.test(trimmed)) {
            return false;
        }

        // No internal sentence period
        if (/[.?!]\s+[A-Z]/.test(trimmed)) {
            return false;
        }

        // Subheadings have title-like capitalization: at least 35% of words start with capital letter
        if (words.length >= 3) {
            const capWords = words.filter(w => /^[A-Z\u00C0-\u00DE\u0100-\u017E\u1E00-\u1E90]/.test(w));
            if (capWords.length / words.length < 0.35) {
                return false;
            }
        }

        return true;
    }

    function formatPurportParagraphs(purportText, fieldHighlights, isProseRecord) {
        if (!purportText) return '';
        const paras = purportText.split(/\r?\n\s*\r?\n/);
        return paras.map(p => {
            const trimmed = p.trim();
            if (!trimmed) return '';

            if (trimmed.startsWith('### ') || isSubheadingParagraph(trimmed, isProseRecord)) {
                const subheading = trimmed.startsWith('### ') ? trimmed.slice(4).trim() : trimmed;
                let hl = applyHighlights(subheading, fieldHighlights);
                hl = linkifyScriptureReferences(hl);
                return `<h3 class="prose-subheading">${hl}</h3>`;
            }

            if (isQuotedVerse(trimmed)) {
                return formatQuotedVerseBlock(trimmed, fieldHighlights);
            }


            // Check if paragraph contains conversation dialogue (lines starting with Speaker:)
            const rawLines = trimmed.split(/\r?\n/).map(l => l.trim()).filter(l => l.length > 0);
            const hasSpeaker = rawLines.some(l => {
                const m = l.match(dialogueSpeakerRegex);
                return m && !speakerIgnoreKeywords.test(m[1]);
            });

            if (hasSpeaker) {
                const rendered = [];
                let currentSpeaker = null;
                let currentText = [];

                function flushCurrent() {
                    if (currentSpeaker) {
                        const speechText = currentText.join(' ');
                        let hl = applyHighlights(speechText, fieldHighlights);
                        hl = linkifyScriptureReferences(hl);
                        rendered.push(`<p class="conversation-speech"><strong class="speaker-name">${escapeHtml(currentSpeaker)}:</strong> ${hl}</p>`);
                        currentSpeaker = null;
                        currentText = [];
                    } else if (currentText.length > 0) {
                        const normalText = currentText.join(' ');
                        let hl = applyHighlights(normalText, fieldHighlights);
                        hl = linkifyScriptureReferences(hl);
                        rendered.push(`<p>${hl}</p>`);
                        currentText = [];
                    }
                }

                for (const line of rawLines) {
                    const sm = line.match(dialogueSpeakerRegex);
                    if (sm && !speakerIgnoreKeywords.test(sm[1])) {
                        flushCurrent();
                        currentSpeaker = sm[1];
                        if (sm[2]) currentText.push(sm[2]);
                    } else {
                        currentText.push(line);
                    }
                }
                flushCurrent();
                return rendered.join('');
            }

            // Regular prose paragraph: normalize single linebreaks to spaces
            const normalized = trimmed.replace(/\r?\n\s*/g, ' ');
            const isQuote = /^["“'‘].*["”'’]$/.test(normalized);
            let highlighted = applyHighlights(normalized, fieldHighlights);
            highlighted = linkifyScriptureReferences(highlighted);

            if (isQuote) {
                return `<p class="purport-quote">${highlighted}</p>`;
            }
            return `<p>${highlighted}</p>`;
        }).join('');
    }

    // ---- Render Single Verse ----

    // ---- Notes & Realizations Helpers ----

    function decodeHtmlEntities(str) {
        // Use a temporary textarea to decode HTML entities natively
        const txt = document.createElement('textarea');
        txt.innerHTML = str;
        return txt.value;
    }

    function renderNoteContent(content) {
        if (!content) return '';
        let html = content;

        // Detect whether content is HTML or plain markdown
        const looksLikeHtml = /<(p|div|h[1-6]|ul|ol|li|strong|em|u|mark|blockquote|table|br)\b/i.test(html);

        if (!looksLikeHtml) {
            // Plain text / Markdown path
            // First decode any HTML entities (e.g. &nbsp; &amp; &lt;) so they don't show as raw code
            html = decodeHtmlEntities(html);
            // Now escape for safe HTML insertion
            html = escapeHtml(html);
            // Headers
            html = html.replace(/^### (.*$)/gim, '<h3>$1</h3>');
            html = html.replace(/^## (.*$)/gim, '<h2>$1</h2>');
            html = html.replace(/^# (.*$)/gim, '<h1>$1</h1>');
            // Bold, Italic, Underline, Highlight
            html = html.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
            html = html.replace(/\*(.*?)\*/g, '<em>$1</em>');
            html = html.replace(/__(.*?)__/g, '<u>$1</u>');
            html = html.replace(/==(.*?)==/g, '<mark class="hl-mark-yellow">$1</mark>');
            // Blockquotes
            html = html.replace(/^\> (.*$)/gim, '<blockquote>$1</blockquote>');
            // Links
            html = html.replace(/\[([^\]]+)\]\((https?:\/\/[^\)]+)\)/g, '<a href="$2" target="_blank" class="external-link" rel="noopener noreferrer">$1 ↗</a>');
            // Line breaks → paragraphs
            const paras = html.split(/\r?\n\s*\r?\n/);
            html = paras.map(p => {
                const trimmed = p.trim();
                if (!trimmed) return '';
                if (/^<(?:h[1-6]|blockquote|table|ul|ol)/i.test(trimmed)) return trimmed;
                return `<p>${trimmed.replace(/\r?\n/g, '<br>')}</p>`;
            }).join('');
        } else {
            // HTML content: ensure &nbsp; and entities are properly handled,
            // and that external links open in new tab
            html = html.replace(/&amp;nbsp;/gi, '\u00a0');  // fix double-escaped &amp;nbsp;
            html = html.replace(/&nbsp;/gi, '\u00a0');       // convert &nbsp; to actual non-breaking space
            html = html.replace(/<a (?!.*?target=)(href="https?:\/\/[^"]*")/gi, '<a target="_blank" class="external-link" $1');
        }

        // Auto-linkify scripture citations and @-mentions
        html = linkifyScriptureReferences(html);

        // Auto-linkify hashtags (#surrender, #guru-tattva)
        html = html.replace(/(?:^|\s)(#([a-zA-Z0-9_\-]+))/g, (match, fullTag, tagName) => {
            return ` <span class="note-hashtag" onclick="reader.onHashtagClick('${tagName}')">${fullTag}</span>`;
        });

        // Auto-linkify bi-directional wiki-links [[Note Title]] or [[BG 1.1]]
        html = html.replace(/\[\[([^\]]+)\]\]/g, (match, linkTarget) => {
            const cleanTarget = linkTarget.trim();
            return `<span class="wiki-link" onclick="reader.onWikiLinkClick('${cleanTarget}')">[[${cleanTarget}]]</span>`;
        });

        return html;
    }

    function renderSingleNoteCardHtml(note, recordKey, verseReference) {
        const id = escapeHtml(note.id || note.Id || '');
        const title = escapeHtml(note.title || note.Title || '');
        const rawContent = note.content || note.Content || '';
        const createdUtc = note.createdUtc || note.CreatedUtc;
        const dateStr = createdUtc ? new Date(createdUtc).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) : (note.CreatedUtcString || '');

        return `
        <article class="note-card" id="note-card-${id}" data-note-id="${id}">
            <header class="note-card-header">
                <div class="note-card-meta">
                    ${title ? `<h3 class="note-card-title">${title}</h3>` : ''}
                    <span class="note-card-date">${escapeHtml(dateStr)}</span>
                </div>
                <div class="note-card-actions">
                    <button class="btn-note-action" onclick="reader.editNote('${escapeHtml(recordKey)}', '${id}')" title="Edit realization">Edit</button>
                    <button class="btn-note-action" onclick="reader.deleteNote('${escapeHtml(recordKey)}', '${id}')" title="Delete realization">Delete</button>
                    <button class="btn-note-action" onclick="reader.exportSingleNote('${id}', '${escapeHtml(verseReference)}')" title="Export as Markdown">Export</button>
                </div>
            </header>
            <div class="note-card-body" id="note-body-content-${id}">
                ${renderNoteContent(rawContent)}
            </div>
            <textarea class="raw-note-data hidden" id="raw-note-${id}" style="display:none;">${escapeHtml(rawContent)}</textarea>
            <input type="hidden" id="raw-note-title-${id}" value="${title}">
        </article>
        `;
    }

    function renderNotesSectionHtml(recordKey, verseReference, notes) {
        const rk = recordKey || '';
        const list = (notes || []).filter(n => {
            const nKey = n.recordKey || n.RecordKey || '';
            const isDel = n.isDeleted || n.IsDeleted || (n.deletedUtc !== undefined && n.deletedUtc !== null);
            return nKey === rk && !isDel;
        });

        let notesListHtml = '';
        if (list.length === 0) {
            notesListHtml = `<div class="notes-empty-state" id="notes-empty-${escapeHtml(rk)}">No personal realizations added for this verse yet. Click "+ Add Realization" to record your notes.</div>`;
        } else {
            notesListHtml = `<div class="notes-list" id="notes-list-${escapeHtml(rk)}">` +
                list.map(n => renderSingleNoteCardHtml(n, rk, verseReference)).join('') +
            `</div>`;
        }

        return `
        <section class="verse-notes-section" id="notes-section-${escapeHtml(rk)}" data-record-key="${escapeHtml(rk)}" data-verse-ref="${escapeHtml(verseReference)}">
            <header class="notes-section-header">
                <div class="notes-section-title-wrap">
                    <span class="notes-section-title">Personal Realizations &amp; Notes</span>
                    <span class="notes-badge" id="notes-count-${escapeHtml(rk)}">${list.length}</span>
                </div>
                <button class="btn-add-note" onclick="reader.openNoteEditor('${escapeHtml(rk)}')">
                    <span class="btn-icon">+</span> Add Realization
                </button>
            </header>

            ${notesListHtml}

            <!-- In-line Rich Note Editor Container -->
            <div class="note-editor-container hidden" id="note-editor-wrap-${escapeHtml(rk)}">
                <div class="note-editor-card">
                    <div class="editor-header">
                        <span class="editor-title" id="editor-mode-label-${escapeHtml(rk)}">New Realization</span>
                        <input type="hidden" id="editor-note-id-${escapeHtml(rk)}" value="">
                        <input type="text" class="editor-title-input" id="editor-title-${escapeHtml(rk)}" placeholder="Title (optional, e.g. Reflections on surrender)...">
                    </div>

                    <!-- Rich Formatting Toolbar -->
                    <div class="editor-toolbar">
                        <div class="toolbar-group">
                            <select class="tb-select" onchange="reader.formatBlock('${escapeHtml(rk)}', this.value); this.selectedIndex = 0;" title="Text Styles & Headings">
                                <option value="">Text Style</option>
                                <option value="p">Paragraph</option>
                                <option value="h1">Heading 1</option>
                                <option value="h2">Heading 2</option>
                                <option value="h3">Heading 3</option>
                            </select>
                        </div>
                        <div class="toolbar-divider"></div>
                        <div class="toolbar-group">
                            <button type="button" class="tb-btn tb-bold" onmousedown="event.preventDefault()" onclick="reader.execFormat('bold')" title="Bold (Ctrl+B)"><b>B</b></button>
                            <button type="button" class="tb-btn tb-italic" onmousedown="event.preventDefault()" onclick="reader.execFormat('italic')" title="Italic (Ctrl+I)"><i>I</i></button>
                            <button type="button" class="tb-btn tb-underline" onmousedown="event.preventDefault()" onclick="reader.execFormat('underline')" title="Underline (Ctrl+U)"><u>U</u></button>
                            <button type="button" class="tb-btn tb-strike" onmousedown="event.preventDefault()" onclick="reader.execFormat('strikeThrough')" title="Strikethrough (Ctrl+Shift+X)"><s>S</s></button>
                            <button type="button" class="tb-btn tb-highlight" onmousedown="event.preventDefault()" onclick="reader.execHighlight()" title="Highlight Text (Ctrl+Shift+H)"><span class="hl-swatch">H</span></button>
                            <button type="button" class="tb-btn tb-code" onmousedown="event.preventDefault()" onclick="reader.insertCode('${escapeHtml(rk)}')" title="Inline Code (Ctrl+Shift+C)"><code>&lt;/&gt;</code></button>
                        </div>
                        <div class="toolbar-divider"></div>
                        <div class="toolbar-group">
                            <button type="button" class="tb-btn" onmousedown="event.preventDefault()" onclick="reader.execFormat('insertUnorderedList')" title="Bullet List">• List</button>
                            <button type="button" class="tb-btn" onmousedown="event.preventDefault()" onclick="reader.execFormat('insertOrderedList')" title="Numbered List">1. List</button>
                            
                            <!-- Table Popover Anchor -->
                            <div class="toolbar-popover-container">
                                <button type="button" class="tb-btn tb-table-btn" id="tb-table-btn-${escapeHtml(rk)}" onmousedown="event.preventDefault()" onclick="reader.toggleTableDialog('${escapeHtml(rk)}')" title="Insert Table (Select rows & columns)">⊞ Table ▾</button>
                                
                                <div class="table-popover hidden" id="table-popover-${escapeHtml(rk)}" onclick="event.stopPropagation()">
                                    <div class="table-popover-header">
                                        <span class="table-popover-title">Insert Table</span>
                                        <span class="table-grid-label" id="table-grid-label-${escapeHtml(rk)}">3 Columns × 3 Rows</span>
                                    </div>
                                    
                                    <!-- 6x6 Interactive Grid Matrix -->
                                    <div class="table-grid-matrix" id="table-grid-matrix-${escapeHtml(rk)}" onmouseleave="reader.onGridMouseLeave('${escapeHtml(rk)}')">
                                        ${generateGridCellsHtml(rk, 6, 6)}
                                    </div>

                                    <!-- Quick Presets -->
                                    <div class="table-presets-row">
                                        <span class="table-presets-label">Presets:</span>
                                        <button type="button" class="btn-table-preset" onclick="reader.insertPresetTable('${escapeHtml(rk)}', 2, 2)">2×2</button>
                                        <button type="button" class="btn-table-preset" onclick="reader.insertPresetTable('${escapeHtml(rk)}', 3, 3)">3×3</button>
                                        <button type="button" class="btn-table-preset" onclick="reader.insertPresetTable('${escapeHtml(rk)}', 4, 2)">4×2</button>
                                        <button type="button" class="btn-table-preset" onclick="reader.insertPresetTable('${escapeHtml(rk)}', 5, 4)">5×4</button>
                                    </div>

                                    <!-- Rows & Columns Inputs -->
                                    <div class="table-popover-row">
                                        <div class="table-input-item">
                                            <label for="table-rows-${escapeHtml(rk)}">Rows:</label>
                                            <input type="number" id="table-rows-${escapeHtml(rk)}" value="3" min="1" max="30" class="table-num-input" oninput="reader.onTableInputChanged('${escapeHtml(rk)}')">
                                        </div>
                                        <div class="table-input-item">
                                            <label for="table-cols-${escapeHtml(rk)}">Cols:</label>
                                            <input type="number" id="table-cols-${escapeHtml(rk)}" value="3" min="1" max="10" class="table-num-input" oninput="reader.onTableInputChanged('${escapeHtml(rk)}')">
                                        </div>
                                    </div>

                                    <div class="table-popover-row table-checkbox-row">
                                        <label class="table-checkbox-label">
                                            <input type="checkbox" id="table-header-${escapeHtml(rk)}" checked>
                                            <span>Include Header Row</span>
                                        </label>
                                    </div>

                                    <div class="table-popover-actions">
                                        <button type="button" class="btn-popover-cancel" onclick="reader.closeTableDialog('${escapeHtml(rk)}')">Cancel</button>
                                        <button type="button" class="btn-popover-insert" onclick="reader.insertCustomTable('${escapeHtml(rk)}')">Insert Table</button>
                                    </div>
                                </div>
                            </div>
                        </div>
                        <div class="toolbar-divider"></div>
                        <div class="toolbar-group">
                            <!-- Link Popover Anchor -->
                            <div class="toolbar-popover-container">
                                <button type="button" class="tb-btn tb-link-btn" id="tb-link-btn-${escapeHtml(rk)}" onmousedown="event.preventDefault()" onclick="reader.toggleLinkDialog('${escapeHtml(rk)}')" title="Insert Hyperlink with Title (Ctrl+K)">🔗 Link ▾</button>
                                
                                <div class="link-popover hidden" id="link-popover-${escapeHtml(rk)}" onclick="event.stopPropagation()">
                                    <div class="link-popover-header">
                                        <span class="link-popover-title">Insert Hyperlink</span>
                                    </div>
                                    
                                    <div class="link-popover-body">
                                        <div class="link-field-group">
                                            <label for="link-text-${escapeHtml(rk)}">Display Title / Text:</label>
                                            <input type="text" id="link-text-${escapeHtml(rk)}" class="link-text-input" placeholder="e.g. Prabhupada Vani Audio" onkeydown="if(event.key==='Enter') reader.insertCustomLink('${escapeHtml(rk)}')">
                                        </div>

                                        <div class="link-field-group">
                                            <label for="link-url-${escapeHtml(rk)}">Web URL:</label>
                                            <input type="text" id="link-url-${escapeHtml(rk)}" class="link-url-input" placeholder="e.g. prabhupadavani.com" onkeydown="if(event.key==='Enter') reader.insertCustomLink('${escapeHtml(rk)}')">
                                        </div>
                                    </div>

                                    <div class="link-popover-actions">
                                        <button type="button" class="btn-popover-cancel" onclick="reader.closeLinkDialog('${escapeHtml(rk)}')">Cancel</button>
                                        <button type="button" class="btn-popover-insert" onclick="reader.insertCustomLink('${escapeHtml(rk)}')">Insert Link</button>
                                    </div>
                                </div>
                            </div>
                            <button type="button" class="tb-btn tb-at-btn" onclick="reader.insertAtPrompt('${escapeHtml(rk)}')" title="Insert Scripture Reference (e.g. @BG 1.1)">@ Ref</button>
                            <button type="button" class="tb-btn" onmousedown="event.preventDefault()" onclick="reader.execFormat('formatBlock', 'blockquote')" title="Quote Block">” Quote</button>
                        </div>
                    </div>

                    <!-- Rich Editable Body with Live Markdown Support -->
                    <div class="editor-content" id="editor-body-${escapeHtml(rk)}" contenteditable="true" oninput="reader.handleEditorInput(event, '${escapeHtml(rk)}')" data-placeholder="Record your reflections, realizations, and notes here... (Type # for H1, - for list, @bg 1.1 for links)"></div>

                    <!-- Editor Action Buttons -->
                    <div class="editor-actions">
                        <button type="button" class="btn-save-note" onclick="reader.saveNote('${escapeHtml(rk)}')">Save Realization</button>
                        <button type="button" class="btn-cancel-note" onclick="reader.closeNoteEditor('${escapeHtml(rk)}')">Cancel</button>
                        <button type="button" class="btn-export-draft" onclick="reader.exportDraft('${escapeHtml(rk)}', '${escapeHtml(verseReference)}')">Export Draft (.md)</button>
                    </div>
                </div>
            </div>
        </section>
        `;
    }

    // ---- Sanskrit Prosody & Meter Chanting Engine ----

    const SANSKRIT_LONG_VOWELS = new Set(['ā', 'ī', 'ū', 'ṝ', 'e', 'ai', 'o', 'au', 'Ā', 'Ī', 'Ū', 'Ṝ', 'E', 'AI', 'O', 'AU']);
    let activePulseInterval = null;
    let activeAudioCtx = null;
    let activePulseButton = null;

    function stopChantingPulse() {
        if (activePulseInterval) {
            clearInterval(activePulseInterval);
            activePulseInterval = null;
        }
        if (activeAudioCtx) {
            try { activeAudioCtx.close(); } catch (e) { }
            activeAudioCtx = null;
        }
        if (activePulseButton) {
            activePulseButton.innerHTML = activePulseButton.getAttribute('data-original-label') || '▶ Play Chanting Pulse';
            activePulseButton.classList.remove('playing');
            activePulseButton = null;
        }
    }

    function toggleChantingPulse(btn, bpm, meterName) {
        if (activePulseInterval) {
            stopChantingPulse();
            return;
        }
        try {
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            if (!AudioCtx) {
                alert('Web Audio API is not supported in this browser.');
                return;
            }
            activeAudioCtx = new AudioCtx();
            const safeBpm = (bpm && bpm > 30 && bpm < 160) ? bpm : 64;
            const intervalMs = (60 / safeBpm) * 1000;
            let beatCount = 0;

            activePulseButton = btn;
            if (!btn.getAttribute('data-original-label')) {
                btn.setAttribute('data-original-label', btn.innerHTML);
            }
            btn.innerHTML = `⏹ Stop Chanting Pulse (${safeBpm} BPM)`;
            btn.classList.add('playing');

            function playChime(freq, gainVal, decay) {
                if (!activeAudioCtx || activeAudioCtx.state === 'closed') return;
                try {
                    const osc = activeAudioCtx.createOscillator();
                    const gain = activeAudioCtx.createGain();
                    osc.type = 'sine';
                    osc.frequency.setValueAtTime(freq, activeAudioCtx.currentTime);
                    gain.gain.setValueAtTime(gainVal, activeAudioCtx.currentTime);
                    gain.gain.exponentialRampToValueAtTime(0.0001, activeAudioCtx.currentTime + decay);
                    osc.connect(gain);
                    gain.connect(activeAudioCtx.destination);
                    osc.start();
                    osc.stop(activeAudioCtx.currentTime + decay);
                } catch (err) { }
            }

            // Initial beat chime
            playChime(660, 0.15, 0.25);
            beatCount = 1;

            activePulseInterval = setInterval(() => {
                beatCount++;
                if (beatCount % 8 === 1) {
                    playChime(587.33, 0.14, 0.28); // Pāda start
                } else if (beatCount % 8 === 0) {
                    playChime(880, 0.18, 0.38); // Caesura / yati pause
                } else {
                    playChime(440, 0.08, 0.10); // Standard beat tick
                }
            }, intervalMs);
        } catch (e) {
            console.error('WebAudio chanting pulse error:', e);
            stopChantingPulse();
        }
    }

    function toggleChantingGuide(recordKey) {
        const drawer = document.getElementById(`chanting-drawer-${recordKey}`);
        const toggleBtn = document.getElementById(`meter-toggle-btn-${recordKey}`);
        if (!drawer) return;
        const isHidden = drawer.classList.contains('hidden');
        if (isHidden) {
            drawer.classList.remove('hidden');
            if (toggleBtn) toggleBtn.innerHTML = 'Hide Recitation Guide ▴';
        } else {
            drawer.classList.add('hidden');
            if (toggleBtn) toggleBtn.innerHTML = 'Show Recitation Guide ▾';
            stopChantingPulse();
        }
    }

    function isSpeakerLine(line) {
        if (!line) return false;
        const trimmed = line.trim().toLowerCase();
        return trimmed.endsWith('uvāca') || trimmed.endsWith('uvaca') ||
               trimmed.endsWith('uvāca:') || trimmed.endsWith('uvaca:') ||
               trimmed.startsWith('śrī-śuka uvāca') || trimmed.startsWith('arjuna uvāca') ||
               trimmed.startsWith('sañjaya uvāca') || trimmed.startsWith('dhṛtarāṣṭra uvāca');
    }

    function tokenizeSanskritSyllables(text) {
        if (!text) return [];
        const lines = text.split('\n');
        const stanzas = [];

        for (const rawLine of lines) {
            const line = rawLine.trim();
            if (!line) continue;
            if (isSpeakerLine(line)) continue;

            const tokens = line.split(/\s+/);
            const lineSyllables = [];

            for (let wIdx = 0; wIdx < tokens.length; wIdx++) {
                const rawWord = tokens[wIdx];
                const cleanWord = rawWord.replace(/^[«"'(]+|[»"')!?,.:;]+$/g, '');
                if (!cleanWord) continue;

                const sylRegex = /([bcdfghjklmnpqrstvwxyzśṣñṅṇṭḍḥṁṃBCDFGHJKLMNPQRSTVWXYZŚṢÑṄṆṬḌ]*)(ai|au|ā|ī|ū|ṝ|e|o|a|i|u|ṛ|ḷ|AI|AU|Ā|Ī|Ū|Ṝ|E|O|A|I|U|Ṛ|Ḷ)([ḥṁṃ]?)/g;
                let match;
                const wordSyllables = [];

                while ((match = sylRegex.exec(cleanWord)) !== null) {
                    if (!match[2]) continue;
                    wordSyllables.push({
                        text: match[0],
                        onset: match[1],
                        nucleus: match[2],
                        coda: match[3]
                    });
                }

                for (let sIdx = 0; sIdx < wordSyllables.length; sIdx++) {
                    const syl = wordSyllables[sIdx];
                    let isGuru = false;

                    if (SANSKRIT_LONG_VOWELS.has(syl.nucleus) || syl.nucleus.toLowerCase() === 'ai' || syl.nucleus.toLowerCase() === 'au') {
                        isGuru = true;
                    } else if (syl.coda && (syl.coda.includes('ṁ') || syl.coda.includes('ṃ') || syl.coda.includes('ḥ'))) {
                        isGuru = true;
                    } else {
                        let nextConsonants = '';
                        if (sIdx + 1 < wordSyllables.length) {
                            nextConsonants = wordSyllables[sIdx + 1].onset;
                        } else if (wIdx + 1 < tokens.length) {
                            const nextClean = tokens[wIdx + 1].replace(/^[«"'(]+|[»"')!?,.:;]+$/g, '');
                            const nextMatch = /^([bcdfghjklmnpqrstvwxyzśṣñṅṇṭḍBCDFGHJKLMNPQRSTVWXYZŚṢÑṄṆṬḌ]+)/.exec(nextClean);
                            if (nextMatch) nextConsonants = nextMatch[1];
                        }

                        const consCount = (nextConsonants.match(/[bcdfghjklmnpqrstvwxyzśṣñṅṇṭḍBCDFGHJKLMNPQRSTVWXYZŚṢÑṄṆṬḌ]/g) || []).length;
                        if (consCount >= 2 || nextConsonants.toLowerCase().includes('kṣ') || nextConsonants.toLowerCase().includes('jñ')) {
                            isGuru = true;
                        }
                    }

                    if (sIdx === wordSyllables.length - 1 && wIdx === tokens.length - 1) {
                        isGuru = true;
                    }

                    lineSyllables.push({
                        text: syl.text,
                        weight: isGuru ? 'guru' : 'laghu',
                        symbol: isGuru ? '–' : '⏑'
                    });
                }
            }

            if (lineSyllables.length > 0) {
                stanzas.push(lineSyllables);
            }
        }

        return stanzas;
    }

    function detectSanskritMeter(stanzas) {
        if (!stanzas || stanzas.length === 0) return null;

        const lineCounts = stanzas.map(s => s.length);
        const totalSyllables = lineCounts.reduce((a, b) => a + b, 0);
        const lineCount = stanzas.length;
        const avgPerLine = totalSyllables / lineCount;

        if (totalSyllables >= 30 && totalSyllables <= 34) {
            return {
                name: 'Anuṣṭubh (Śloka)',
                syllables: totalSyllables,
                padaStructure: lineCount === 2 ? '16 + 16 (4 quarters of 8)' : '8 + 8 + 8 + 8',
                bpm: 68,
                caesuraDesc: 'Caesura (yati) after each 8th syllable (half-line and full-line pauses).',
                description: 'The supreme classical 32-syllable Vedic meter. Standard throughout the Bhagavad-gītā, Śrīmad-Bhāgavatam, and Mahābhārata.',
                quarters: 4,
                syllablesPerPada: 8
            };
        }

        if ((totalSyllables >= 42 && totalSyllables <= 46) || (lineCount === 4 && lineCounts.every(c => c >= 10 && c <= 12))) {
            return {
                name: 'Triṣṭubh (Upajāti / Indravajrā)',
                syllables: totalSyllables,
                padaStructure: '11 + 11 + 11 + 11',
                bpm: 62,
                caesuraDesc: 'Caesura after 5th syllable and at the end of each 11-syllable pāda.',
                description: 'Elevated 11-syllable Vedic meter. Used for profound philosophical prayers and the Universal Form revelatory verses (BG 11).',
                quarters: 4,
                syllablesPerPada: 11
            };
        }

        if (totalSyllables >= 47 && totalSyllables <= 50) {
            return {
                name: 'Jagatī (Vaṁśastha)',
                syllables: totalSyllables,
                padaStructure: '12 + 12 + 12 + 12',
                bpm: 60,
                caesuraDesc: 'Caesura at the 5th and 12th syllables.',
                description: 'Graceful 12-syllable classical Sanskrit meter with rolling melodic rhythm.',
                quarters: 4,
                syllablesPerPada: 12
            };
        }

        if (totalSyllables >= 54 && totalSyllables <= 58) {
            return {
                name: 'Vasantatilakā',
                syllables: totalSyllables,
                padaStructure: '14 + 14 + 14 + 14',
                bpm: 58,
                caesuraDesc: 'Caesura after the 8th and 14th syllables.',
                description: '\'The Spring Blossom\' lyrical 14-syllable meter celebrated for devotional warmth and sweetness.',
                quarters: 4,
                syllablesPerPada: 14
            };
        }

        if (totalSyllables >= 59 && totalSyllables <= 62) {
            return {
                name: 'Mālinī',
                syllables: totalSyllables,
                padaStructure: '15 + 15 + 15 + 15',
                bpm: 56,
                caesuraDesc: 'Caesura after the 8th syllable (8 + 7 yati).',
                description: '\'The Garland-Wearer\' flowing 15-syllable lyrical meter with rhythmic pause at the 8th syllable.',
                quarters: 4,
                syllablesPerPada: 15
            };
        }

        if (totalSyllables >= 66 && totalSyllables <= 70) {
            return {
                name: 'Mandākrāntā',
                syllables: totalSyllables,
                padaStructure: '17 + 17 + 17 + 17',
                bpm: 54,
                caesuraDesc: 'Caesura at the 4th, 10th (4+6), and 17th syllables (4, 6, 7 yati).',
                description: '\'Slow-Stepping\' sacred 17-syllable meter. The revered meter of Śrī Brahma-saṁhitā (premāñjana-cchurita-bhakti-vilocanena).',
                quarters: 4,
                syllablesPerPada: 17
            };
        }

        if (totalSyllables >= 74 && totalSyllables <= 78) {
            return {
                name: 'Śārdūlavikrīḍita',
                syllables: totalSyllables,
                padaStructure: '19 + 19 + 19 + 19',
                bpm: 52,
                caesuraDesc: 'Caesura after 12th syllable (12 + 7 yati).',
                description: '\'Play of the Tiger\' majestic 19-syllable meter. Standard in Mukunda-mālā-stotra and invocations.',
                quarters: 4,
                syllablesPerPada: 19
            };
        }

        if (totalSyllables >= 26 && totalSyllables <= 29 && lineCount === 2) {
            return {
                name: 'Payāra (Bengali Couplet)',
                syllables: totalSyllables,
                padaStructure: '14 + 14 (8 + 6 yati)',
                bpm: 72,
                caesuraDesc: 'Caesura after the 8th and 14th syllables.',
                description: 'Sacred 14-syllable rhythmic couplet. The signature poetic vehicle of Śrī Caitanya-caritāmṛta.',
                quarters: 2,
                syllablesPerPada: 14
            };
        }

        if (totalSyllables >= 16) {
            return {
                name: `Classical Sanskrit Prosody (${totalSyllables} Syllables)`,
                syllables: totalSyllables,
                padaStructure: `${lineCounts.join(' + ')} syllables`,
                bpm: 64,
                caesuraDesc: 'Breathe at line breaks and punctuation.',
                description: 'Vedic/Classical Sanskrit metered verse.',
                quarters: lineCount,
                syllablesPerPada: Math.round(avgPerLine)
            };
        }

        return null;
    }

    function generateMeterGuideHtml(translitText, recordKey) {
        if (!translitText || translitText.length < 10) return '';
        const stanzas = tokenizeSanskritSyllables(translitText);
        if (!stanzas || stanzas.length === 0) return '';
        const meter = detectSanskritMeter(stanzas);
        if (!meter) return '';

        const escapedKey = escapeHtml(recordKey);

        let stanzasHtml = '';
        for (let l = 0; l < stanzas.length; l++) {
            const line = stanzas[l];
            let lineHtml = `<div class="chanting-line"><span class="line-num">${l + 1}</span>`;

            for (let s = 0; s < line.length; s++) {
                const syl = line[s];
                const weightClass = syl.weight;
                const weightLabel = syl.weight === 'guru' ? 'Guru (Heavy: 2 mātrās)' : 'Laghu (Light: 1 mātrā)';
                lineHtml += `<span class="syllable ${weightClass}" title="${weightLabel}"><span class="syl-text">${escapeHtml(syl.text)}</span><span class="syl-symbol">${syl.symbol}</span></span>`;

                if (meter.name.startsWith('Anuṣṭubh') && (s + 1) === 8 && s + 1 < line.length) {
                    lineHtml += `<span class="caesura-pause" title="Yati (Pause & Breathe)">|</span>`;
                } else if (meter.name.startsWith('Triṣṭubh') && (s + 1) === 5 && s + 1 < line.length) {
                    lineHtml += `<span class="caesura-pause" title="Yati (Pause)">|</span>`;
                } else if (meter.name.startsWith('Mandākrāntā') && ((s + 1) === 4 || (s + 1) === 10) && s + 1 < line.length) {
                    lineHtml += `<span class="caesura-pause" title="Yati (Pause)">|</span>`;
                } else if (meter.name.startsWith('Śārdūlavikrīḍita') && (s + 1) === 12 && s + 1 < line.length) {
                    lineHtml += `<span class="caesura-pause" title="Yati (Pause)">|</span>`;
                } else if (meter.name.startsWith('Payāra') && (s + 1) === 8 && s + 1 < line.length) {
                    lineHtml += `<span class="caesura-pause" title="Yati (Pause)">|</span>`;
                }
            }
            const endPause = (l === stanzas.length - 1) ? '||' : '|';
            lineHtml += `<span class="caesura-pause end-line" title="Pāda End">${endPause}</span></div>`;
            stanzasHtml += lineHtml;
        }

        return `
        <div class="meter-guide-container" id="meter-container-${escapedKey}">
            <div class="meter-badge" onclick="reader.toggleChantingGuide('${escapedKey}')" title="Click to view syllable weights and recitation guide">
                <span class="meter-badge-name">${escapeHtml(meter.name)}</span>
                <span class="meter-badge-divider">•</span>
                <span class="meter-badge-syllables">${meter.syllables} Syllables</span>
                <span class="meter-badge-toggle" id="meter-toggle-btn-${escapedKey}">Show Recitation Guide ▾</span>
            </div>
            <div class="chanting-guide-drawer hidden" id="chanting-drawer-${escapedKey}">
                <div class="chanting-guide-header">
                    <div class="chanting-guide-info">
                        <div class="meter-title-row">
                            <h4 class="chanting-meter-title">${escapeHtml(meter.name)}</h4>
                            <span class="meter-pada-pill">${escapeHtml(meter.padaStructure)}</span>
                        </div>
                        <p class="chanting-meter-desc">${escapeHtml(meter.description)}</p>
                        <div class="chanting-caesura-hint"><strong>Breath &amp; Pauses (Yati):</strong> ${escapeHtml(meter.caesuraDesc)}</div>
                    </div>
                    <div class="chanting-pulse-controls">
                        <button class="btn-chanting-pulse" id="pulse-btn-${escapedKey}" onclick="reader.toggleChantingPulse(this, ${meter.bpm}, '${escapeHtml(meter.name)}')">
                            ▶ Play Chanting Pulse (${meter.bpm} BPM)
                        </button>
                    </div>
                </div>
                <div class="chanting-legend">
                    <span class="legend-item"><span class="legend-chip guru">–</span> <strong>Guru</strong> (Heavy, 2 beats)</span>
                    <span class="legend-item"><span class="legend-chip laghu">⏑</span> <strong>Laghu</strong> (Light, 1 beat)</span>
                    <span class="legend-item"><span class="legend-chip pause">|</span> <strong>Caesura (Yati)</strong> (Breath Pause)</span>
                </div>
                <div class="chanting-stanzas">
                    ${stanzasHtml}
                </div>
            </div>
        </div>
        `;
    }

    function renderSongCardHtml(record, songData, options = {}, hlMap = {}, notes = [], isSingleVerse = true) {
        const showTranslit = options.showTransliteration !== false;
        const showSynonyms = options.showSynonyms !== false;
        const showPurport = options.showPurport !== false;
        const showTranslation = options.showTranslation !== false;

        const bannerTitle = songData.bannerTitle || record.Title || record.Reference;
        const subtitle = songData.subtitle || '';
        const stanzas = songData.stanzas || [];
        const purport = songData.purport || '';

        let html = `
        <article class="verse-card single-verse song-card" id="verse-${escapeHtml(record.RecordKey)}" data-record-key="${escapeHtml(record.RecordKey)}">
            <header class="song-header-banner">
                <div class="song-header-reference">${escapeHtml(record.Reference || record.RecordKey)}</div>
                <h1 class="song-header-title">${escapeHtml(bannerTitle)}</h1>
                ${subtitle ? `<div class="song-header-subtitle">${escapeHtml(subtitle)}</div>` : ''}
            </header>
        `;

        if (stanzas && stanzas.length > 0) {
            html += `<div class="song-stanzas-container">`;
            for (let i = 0; i < stanzas.length; i++) {
                const st = stanzas[i];
                const label = st.label || '';
                const lines = st.lines || [];
                const syns = st.synonyms || '';
                const trans = st.translation || '';

                html += `
                <div class="song-stanza-block" id="stanza-${escapeHtml(record.RecordKey)}-${i + 1}">
                    ${label ? `<div class="song-stanza-label">${escapeHtml(label)}</div>` : ''}
                `;

                if (lines.length > 0 && showTranslit) {
                    const linesHtml = lines.map(line => {
                        let lHtml = applyHighlights(line, hlMap['transliteration']);
                        lHtml = linkifyScriptureReferences(lHtml);
                        return `<div class="song-verse-line">${lHtml}</div>`;
                    }).join('');
                    html += `<div class="song-verse-stanza">${linesHtml}</div>`;
                }

                if (syns && showSynonyms) {
                    html += `
                    <div class="song-section-label">SYNONYMS</div>
                    <div class="verse-synonyms song-synonyms">${formatSynonyms(syns, hlMap['synonyms'])}</div>
                    `;
                }

                if (trans && showTranslation) {
                    const transHl = linkifyScriptureReferences(applyHighlights(trans, hlMap['translation']));
                    const showTransLabel = !lines.length && !syns;
                    html += `
                    ${showTransLabel ? `<div class="song-section-label">TRANSLATION</div>` : ''}
                    <div class="verse-translation song-translation">${transHl}</div>
                    `;
                }

                html += `</div>`;
            }
            html += `</div>`;
        }

        if (purport && showPurport) {
            html += `
            <div class="section-label song-purport-label">Purport</div>
            <div class="verse-purport song-purport-body" data-field="Purport">${formatPurportParagraphs(purport, hlMap['purport'], false)}</div>
            `;
        }

        if (isSingleVerse) {
            html += renderNotesSectionHtml(record.RecordKey, record.Reference || record.RecordKey, notes);
        }

        html += `</article>`;
        return html;
    }

    // ---- Render Single Verse ----

    function renderVerse(record, options = {}, highlights = [], notes = []) {
        if (!record) return;
        stopChantingPulse();
        clearFindSearch();

        const showTranslit = options.showTransliteration !== false;
        const showSynonyms = options.showSynonyms !== false;
        const showPurport = options.showPurport !== false;
        const showPronunciation = options.showPronunciationGuide !== false;

        const hlMap = groupHighlightsByField(highlights, record.RecordKey);

        let songData = null;
        if (record.Purports && (record.Purports.startsWith('{"type":"song"') || record.Purports.startsWith('{"type": "song"'))) {
            try {
                songData = JSON.parse(record.Purports);
            } catch (e) { }
        }

        if (songData) {
            contentEl.innerHTML = renderSongCardHtml(record, songData, options, hlMap, notes, true);
            window.scrollTo({ top: 0, behavior: 'instant' });
            return;
        }

        const isPurportRecord = record.RecordType === 'Purport';
        const isProseRecord = isPurportRecord || record.BookKey === 'SPL' || (!record.Devanagari && !record.Transliteration && !record.Synonyms && (!record.Translation || record.BookKey === 'SPL'));
        const displayTitle = isPurportRecord ? `${record.Reference || record.RecordKey} — ${record.Title || ''}` : (record.Title || record.Reference || record.RecordKey);
        const hasDistinctCitation = Boolean(
            !isProseRecord &&
            record.Title &&
            record.Title.trim() &&
            record.Title.trim().toLowerCase() !== (record.Reference || '').trim().toLowerCase() &&
            record.Title.trim().toLowerCase() !== record.RecordKey.toLowerCase()
        );

        let html = `
        <article class="verse-card single-verse ${isProseRecord ? 'prose-chapter-card' : ''}" id="verse-${escapeHtml(record.RecordKey)}" data-record-key="${escapeHtml(record.RecordKey)}">
            <header class="${isProseRecord ? 'chapter-header-card' : 'verse-header'}">
                <div class="verse-header-content">
                    <h1 class="${isProseRecord ? 'chapter-title' : 'verse-reference'}">${escapeHtml(isProseRecord ? displayTitle : (record.Reference || record.RecordKey))}</h1>
                    ${hasDistinctCitation ? `
                    <div class="verse-citation-badge-container">
                        <button class="verse-citation-pill" onclick="reader.onNavigateScripture(event, '${escapeHtml(record.Title)}')" title="Navigate or search scripture: ${escapeHtml(record.Title)}">
                            <span class="citation-icon">📖</span>
                            <span class="citation-source-label">Source Scripture:</span>
                            <strong class="citation-title">${escapeHtml(record.Title)}</strong>
                            <span class="citation-arrow">↗</span>
                        </button>
                    </div>` : ''}
                </div>
            </header>
        `;

        if (record.Devanagari) {
            html += `<div class="verse-devanagari" data-field="Devanagari">${applyHighlights(record.Devanagari, hlMap['devanagari'])}</div>`;
        }

        if (record.Transliteration && showTranslit) {
            html += `<div class="verse-transliteration" data-field="Transliteration">${applyHighlights(record.Transliteration, hlMap['transliteration'])}</div>`;
            if (showPronunciation) {
                const meterGuideHtml = generateMeterGuideHtml(record.Transliteration, record.RecordKey);
                if (meterGuideHtml) {
                    html += meterGuideHtml;
                }
            }
        }

        if (record.Synonyms && showSynonyms) {
            html += `
            <div class="section-label">Synonyms</div>
            <div class="verse-synonyms" data-field="Synonyms">${formatSynonyms(record.Synonyms, hlMap['synonyms'])}</div>
            `;
        }

        if (!isProseRecord && (record.CleanTranslation || record.Translation)) {
            const trans = record.CleanTranslation || record.Translation;
            const transHighlighted = linkifyScriptureReferences(applyHighlights(trans, hlMap['translation']));
            html += `
            <div class="section-label">Translation</div>
            <div class="verse-translation" data-field="Translation">${transHighlighted}</div>
            `;
        }

        if (record.Purports && showPurport) {
            if (!isProseRecord) {
                html += `<div class="section-label">Purport</div>`;
            }
            const purportBody = isProseRecord ? sanitizeProsePurport(record.Purports, displayTitle, displayTitle) : record.Purports;
            html += `
            <div class="verse-purport ${isProseRecord ? 'prose-body' : ''}" data-field="Purport">${formatPurportParagraphs(purportBody, hlMap['purport'], isProseRecord)}</div>
            `;

        }

        // In-line Realizations & Research Notes
        html += renderNotesSectionHtml(record.RecordKey, record.Reference || record.RecordKey, notes);

        html += `</article>`;
        contentEl.innerHTML = html;
        window.scrollTo({ top: 0, behavior: 'instant' });
    }

    // ---- Render Continuous Chapter ----

    function renderChapter(chapterTitle, subtitle, records, options = {}, highlights = [], notes = []) {
        if (!records || !Array.isArray(records)) return;
        stopChantingPulse();
        clearFindSearch();

        const showTranslit = options.showTransliteration !== false;
        const showSynonyms = options.showSynonyms !== false;
        const showPurport = options.showPurport !== false;
        const showPronunciation = options.showPronunciationGuide !== false;

        const hideSubtitle = !subtitle || subtitle.trim().length === 0 ||
            subtitle.trim().toLowerCase() === chapterTitle.trim().toLowerCase() ||
            chapterTitle.trim().toLowerCase().includes(subtitle.trim().toLowerCase()) ||
            subtitle.trim().toLowerCase().includes(chapterTitle.trim().toLowerCase());

        let html = `
        <header class="chapter-header-card">
            <h1 class="chapter-title">${escapeHtml(chapterTitle)}</h1>
            ${hideSubtitle ? '' : `<div class="chapter-subtitle">${escapeHtml(subtitle)}</div>`}
        </header>
        `;

        for (const record of records) {
            const hlMap = groupHighlightsByField(highlights, record.RecordKey);

            let songData = null;
            if (record.Purports && (record.Purports.startsWith('{"type":"song"') || record.Purports.startsWith('{"type": "song"'))) {
                try {
                    songData = JSON.parse(record.Purports);
                } catch (e) { }
            }

            if (songData) {
                html += renderSongCardHtml(record, songData, options, hlMap, notes, false);
                continue;
            }

            const isPurportRecord = record.RecordType === 'Purport';
            const isProseRecord = isPurportRecord || record.BookKey === 'SPL' || (!record.Devanagari && !record.Transliteration && !record.Synonyms && (!record.Translation || record.BookKey === 'SPL'));
            const displayTitle = isPurportRecord ? `${record.Reference || record.RecordKey} — ${record.Title || ''}` : (isProseRecord ? (record.Title || record.Reference || record.RecordKey) : (record.Reference || record.RecordKey));
            const isSingleProseRecord = isProseRecord && records.length === 1;
            const hasDistinctCitation = Boolean(
                !isProseRecord &&
                record.Title &&
                record.Title.trim() &&
                record.Title.trim().toLowerCase() !== (record.Reference || '').trim().toLowerCase() &&
                record.Title.trim().toLowerCase() !== record.RecordKey.toLowerCase()
            );

            html += `
            <article class="verse-card ${isProseRecord ? 'prose-chapter-card' : ''}" id="verse-${escapeHtml(record.RecordKey)}" data-record-key="${escapeHtml(record.RecordKey)}">
            `;

            if (!isSingleProseRecord) {
                html += `
                <header class="${isProseRecord ? 'chapter-header-card' : 'verse-header'}">
                    <div class="verse-header-content">
                        <h2 class="${isProseRecord ? 'chapter-title' : 'verse-reference'}">${escapeHtml(displayTitle)}</h2>
                        ${hasDistinctCitation ? `
                        <div class="verse-citation-badge-container">
                            <button class="verse-citation-pill" onclick="reader.onNavigateScripture(event, '${escapeHtml(record.Title)}')" title="Navigate or search scripture: ${escapeHtml(record.Title)}">
                                <span class="citation-icon">📖</span>
                                <span class="citation-source-label">Source Scripture:</span>
                                <strong class="citation-title">${escapeHtml(record.Title)}</strong>
                                <span class="citation-arrow">↗</span>
                            </button>
                        </div>` : ''}
                    </div>
                    <button class="btn-focus-verse" onclick="reader.onFocusVerse('${escapeHtml(record.RecordKey)}')">Focus Verse</button>
                </header>
                `;
            }

            if (record.Devanagari) {
                html += `<div class="verse-devanagari" data-field="Devanagari">${applyHighlights(record.Devanagari, hlMap['devanagari'])}</div>`;
            }

            if (record.Transliteration && showTranslit) {
                html += `<div class="verse-transliteration" data-field="Transliteration">${applyHighlights(record.Transliteration, hlMap['transliteration'])}</div>`;
                if (showPronunciation) {
                    const meterGuideHtml = generateMeterGuideHtml(record.Transliteration, record.RecordKey);
                    if (meterGuideHtml) {
                        html += meterGuideHtml;
                    }
                }
            }

            if (record.Synonyms && showSynonyms) {
                html += `
                <div class="section-label">Synonyms</div>
                <div class="verse-synonyms" data-field="Synonyms">${formatSynonyms(record.Synonyms, hlMap['synonyms'])}</div>
                `;
            }

            if (!isProseRecord && (record.CleanTranslation || record.Translation)) {
                const trans = record.CleanTranslation || record.Translation;
                const transHighlighted = linkifyScriptureReferences(applyHighlights(trans, hlMap['translation']));
                html += `
                <div class="section-label">Translation</div>
                <div class="verse-translation" data-field="Translation">${transHighlighted}</div>
                `;
            }

            if (record.Purports && showPurport) {
                if (!isProseRecord) {
                    html += `<div class="section-label">Purport</div>`;
                }
                const purportBody = isProseRecord ? sanitizeProsePurport(record.Purports, displayTitle, chapterTitle) : record.Purports;
                html += `
                <div class="verse-purport ${isProseRecord ? 'prose-body' : ''}" data-field="Purport">${formatPurportParagraphs(purportBody, hlMap['purport'], isProseRecord)}</div>
                `;

            }

            // In-line Realizations & Research Notes
            html += renderNotesSectionHtml(record.RecordKey, record.Reference || record.RecordKey, notes);

            html += `</article>`;
        }

        contentEl.innerHTML = html;
    }

    function scrollToVerse(refOrKey) {
        if (!refOrKey) return;
        let el = document.getElementById(`verse-${refOrKey}`);
        if (!el) {
            const refs = document.querySelectorAll('.verse-reference');
            for (const r of refs) {
                if (r.textContent.trim().toLowerCase() === refOrKey.trim().toLowerCase()) {
                    el = r.closest('.verse-card');
                    break;
                }
            }
        }

        if (el) {
            el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    }

    // Focus verse clicked in chapter view
    window.reader.onFocusVerse = function (recordKey) {
        notifyHost({ action: 'focus_verse', recordKey: recordKey });
    };

    // ---- In-Page Find Search (Ctrl+F) ----

    function clearFindSearch() {
        currentMatches = [];
        activeMatchIndex = -1;
        const marks = document.querySelectorAll('mark.find-match');
        marks.forEach(mark => {
            const parent = mark.parentNode;
            if (parent) {
                parent.replaceChild(document.createTextNode(mark.textContent), mark);
                parent.normalize();
            }
        });
    }

    const IAST_REGEX_MAP = {
        'a': '[aā]', 'A': '[AĀ]', 'ā': '[aā]', 'Ā': '[AĀ]',
        'i': '[iī]', 'I': '[IĪ]', 'ī': '[iī]', 'Ī': '[IĪ]',
        'u': '[uū]', 'U': '[UŪ]', 'ū': '[uū]', 'Ū': '[UŪ]',
        'r': '[rṛṝ]', 'R': '[RṚṜ]', 'ṛ': '[rṛṝ]', 'Ṛ': '[RṚṜ]', 'ṝ': '[rṛṝ]', 'Ṝ': '[RṚṜ]',
        'l': '[lḷḹ]', 'L': '[LḶḸ]', 'ḷ': '[lḷḹ]', 'Ḷ': '[LḶḸ]', 'ḹ': '[lḷḹ]', 'Ḹ': '[LḶḸ]',
        'e': '[eē]', 'E': '[EĒ]', 'ē': '[eē]', 'Ē': '[EĒ]',
        'o': '[oō]', 'O': '[OŌ]', 'ō': '[oō]', 'Ō': '[OŌ]',
        'm': '[mṁṃ]', 'M': '[MṀṂ]', 'ṁ': '[mṁṃ]', 'Ṁ': '[MṀṂ]', 'ṃ': '[mṁṃ]', 'Ṃ': '[MṀṂ]',
        'h': '[hḥ]', 'H': '[HḤ]', 'ḥ': '[hḥ]', 'Ḥ': '[HḤ]',
        'n': '[nñṅṇ]', 'N': '[NÑṄṆ]', 'ñ': '[nñṅṇ]', 'Ñ': '[NÑṄṆ]', 'ṅ': '[nñṅṇ]', 'Ṅ': '[NÑṄṆ]', 'ṇ': '[nñṅṇ]', 'Ṇ': '[NÑṄṆ]',
        't': '[tṭ]', 'T': '[TṬ]', 'ṭ': '[tṭ]', 'Ṭ': '[TṬ]',
        'd': '[dḍ]', 'D': '[DḌ]', 'ḍ': '[dḍ]', 'Ḍ': '[DḌ]',
        's': '[sśṣ]', 'S': '[SŚṢ]', 'ś': '[sśṣ]', 'Ś': '[SŚṢ]', 'ṣ': '[sśṣ]', 'Ṣ': '[SŚṢ]'
    };

    function buildIastRegexPattern(term) {
        let res = '';
        for (let i = 0; i < term.length; i++) {
            const ch = term[i];
            if (IAST_REGEX_MAP[ch]) {
                res += IAST_REGEX_MAP[ch];
            } else if (/[.*+?^${}()|[\]\\]/.test(ch)) {
                res += '\\' + ch;
            } else {
                res += ch;
            }
        }
        return res;
    }

    function findSearch(query, options = {}) {
        clearFindSearch();
        if (!query || query.trim().length === 0) {
            return { totalMatches: 0, activeIndex: -1, matches: [] };
        }

        const trimmed = query.trim();
        const matchCase = !!options.matchCase;
        const matchWord = !!options.matchWord;

        const walker = document.createTreeWalker(
            contentEl,
            NodeFilter.SHOW_TEXT,
            {
                acceptNode: (node) => {
                    if (!node.textContent || node.textContent.trim().length === 0) {
                        return NodeFilter.FILTER_REJECT;
                    }
                    if (node.parentNode && (node.parentNode.tagName === 'BUTTON' || node.parentNode.tagName === 'MARK' || node.parentNode.classList.contains('section-label'))) {
                        return NodeFilter.FILTER_REJECT;
                    }
                    return NodeFilter.FILTER_ACCEPT;
                }
            }
        );

        const iastPattern = buildIastRegexPattern(trimmed);
        const patternStr = matchWord
            ? `(?:(?<=^|[^\\p{L}\\p{N}_])|(?<=\\b))${iastPattern}(?=(?:[^\\p{L}\\p{N}_]|$|\\b))`
            : iastPattern;
        const flags = (matchCase ? 'g' : 'gi') + 'u';

        let testRegex;
        try {
            testRegex = new RegExp(iastPattern, (matchCase ? '' : 'i') + 'u');
        } catch (e) {
            testRegex = new RegExp(trimmed.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), matchCase ? '' : 'i');
        }

        const nodesToProcess = [];
        let currentNode;
        while ((currentNode = walker.nextNode())) {
            if (testRegex.test(currentNode.textContent)) {
                nodesToProcess.push(currentNode);
            }
        }

        const matchSummaries = [];
        let matchCount = 0;

        for (const textNode of nodesToProcess) {
            const parent = textNode.parentNode;
            if (!parent) continue;

            const text = textNode.textContent;
            let regex;
            try {
                regex = new RegExp(patternStr, flags);
            } catch (e) {
                regex = new RegExp(escapedTerm, matchCase ? 'g' : 'gi');
            }

            let match;
            let lastIndex = 0;
            const fragment = document.createDocumentFragment();

            while ((match = regex.exec(text)) !== null) {
                if (match.index > lastIndex) {
                    fragment.appendChild(document.createTextNode(text.substring(lastIndex, match.index)));
                }

                const mark = document.createElement('mark');
                mark.className = 'find-match';
                mark.textContent = match[0];
                mark.dataset.matchIndex = matchCount;

                const verseCard = parent.closest('.verse-card');
                const fieldEl = parent.closest('[data-field]');
                const referenceEl = verseCard ? verseCard.querySelector('.verse-reference, .chapter-title') : null;
                const reference = referenceEl ? referenceEl.textContent.trim() : '';

                matchSummaries.push({
                    index: matchCount,
                    reference: reference,
                    field: fieldEl ? fieldEl.dataset.field : ''
                });

                currentMatches.push(mark);
                fragment.appendChild(mark);

                lastIndex = regex.lastIndex;
                matchCount++;

                if (regex.lastIndex === match.index) {
                    regex.lastIndex++;
                }
            }

            if (lastIndex < text.length) {
                fragment.appendChild(document.createTextNode(text.substring(lastIndex)));
            }

            if (fragment.childNodes.length > 0) {
                parent.replaceChild(fragment, textNode);
            }
        }

        if (currentMatches.length > 0) {
            setActiveFindMatch(0);
        }

        return {
            totalMatches: currentMatches.length,
            activeIndex: activeMatchIndex,
            matches: matchSummaries
        };
    }

    function setActiveFindMatch(index) {
        if (currentMatches.length === 0) return;
        if (index < 0 || index >= currentMatches.length) return;

        if (activeMatchIndex >= 0 && activeMatchIndex < currentMatches.length) {
            currentMatches[activeMatchIndex].classList.remove('active');
        }

        activeMatchIndex = index;
        const target = currentMatches[activeMatchIndex];
        target.classList.add('active');
        target.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }

    // ---- Theming & Appearance Bridge ----

    function setTheme(theme) {
        if (!theme) return;
        const root = document.documentElement;

        if (theme.backgroundColor) {
            root.style.setProperty('--bg-color', theme.backgroundColor);
            document.body.style.backgroundColor = theme.backgroundColor;
            root.style.backgroundColor = theme.backgroundColor;

            // Compute luminance of background to set optimal Sanskrit verse yellow color
            const hex = theme.backgroundColor.replace('#', '');
            if (hex.length >= 6) {
                const r = parseInt(hex.substring(0, 2), 16);
                const g = parseInt(hex.substring(2, 4), 16);
                const b = parseInt(hex.substring(4, 6), 16);
                const lum = (0.299 * r + 0.587 * g + 0.114 * b);
                if (lum > 140) {
                    // Light theme: rich deep amber saffron for high contrast on white/light
                    root.style.setProperty('--color-sanskrit-verse', '#945B00');
                } else {
                    // Dark/Green theme: luminous warm golden yellow
                    root.style.setProperty('--color-sanskrit-verse', '#F5C542');
                }
            }
        }
        if (theme.primaryTextColor) {
            root.style.setProperty('--text-primary', theme.primaryTextColor);
            document.body.style.color = theme.primaryTextColor;
        }
        if (theme.secondaryTextColor) root.style.setProperty('--text-secondary', theme.secondaryTextColor);
        if (theme.textTertiaryColor) root.style.setProperty('--text-tertiary', theme.textTertiaryColor);
        if (theme.accentColor) root.style.setProperty('--accent-color', theme.accentColor);
        if (theme.cardBackground) root.style.setProperty('--card-bg', theme.cardBackground);
        if (theme.cardBorder) root.style.setProperty('--card-border', theme.cardBorder);
    }

    function setTextBrightness(percent) {
        const val = Math.max(20, Math.min(100, percent)) / 100.0;
        document.documentElement.style.setProperty('--text-brightness', val.toFixed(2));
    }

    function setFontSizes(sizes) {
        if (!sizes) return;
        const root = document.documentElement;
        if (sizes.verse) root.style.setProperty('--font-size-verse', sizes.verse + 'px');
        if (sizes.transliteration) root.style.setProperty('--font-size-translit', sizes.transliteration + 'px');
        if (sizes.synonyms) root.style.setProperty('--font-size-synonyms', sizes.synonyms + 'px');
        if (sizes.translation) root.style.setProperty('--font-size-translation', sizes.translation + 'px');
        if (sizes.purport) root.style.setProperty('--font-size-purport', sizes.purport + 'px');
    }

    function setLineHeight(height) {
        if (height) document.documentElement.style.setProperty('--line-height-purport', height.toString());
    }

    function setContentMaxWidth(widthPx) {
        if (widthPx) document.documentElement.style.setProperty('--content-max-width', widthPx + 'px');
    }

    // ---- Selection & Floating Toolbar ----

    // Crucial: prevent mousedown on toolbar from clearing document selection
    toolbarEl.addEventListener('mousedown', (e) => {
        e.preventDefault();
    });

    let selRafId = null;
    document.addEventListener('selectionchange', () => {
        if (selRafId) cancelAnimationFrame(selRafId);
        selRafId = requestAnimationFrame(handleSelectionChange);
    });

    function handleSelectionChange() {
        const sel = window.getSelection();
        if (!sel || sel.isCollapsed || sel.rangeCount === 0) {
            hideToolbar();
            currentSelectionInfo = null;
            return;
        }

        const text = sel.toString().trim();
        if (text.length === 0) {
            hideToolbar();
            currentSelectionInfo = null;
            return;
        }

        const range = sel.getRangeAt(0);
        const rect = range.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) {
            hideToolbar();
            return;
        }

        let container = range.commonAncestorContainer;
        if (container.nodeType === Node.TEXT_NODE) container = container.parentNode;

        // Never show scripture selection toolbar inside notes or the note editor
        if (container.closest('.verse-notes-section') ||
            container.closest('.note-editor-card') ||
            container.closest('.editor-content') ||
            container.closest('.note-card')) {
            hideToolbar();
            currentSelectionInfo = null;
            return;
        }

        const verseCard = container.closest('.verse-card');
        const fieldEl = container.closest('[data-field]');

        if (!verseCard || !fieldEl) {
            hideToolbar();
            return;
        }

        currentSelectionInfo = {
            text: text,
            recordKey: verseCard.dataset.recordKey,
            field: fieldEl ? fieldEl.dataset.field : 'Purport',
            range: range.cloneRange(),
            rect: rect
        };

        showToolbar(rect);
    }

    function showToolbar(rect) {
        toolbarEl.classList.remove('hidden');
        const left = Math.max(140, Math.min(window.innerWidth - 140, rect.left + rect.width / 2));
        let top = rect.top - 14;
        if (top < 50) top = rect.bottom + 40; // If too close to top, show below selection
        toolbarEl.style.left = `${left}px`;
        toolbarEl.style.top = `${top}px`;
    }

    function hideToolbar() {
        toolbarEl.classList.add('hidden');
    }

    // Toolbar button clicks
    document.querySelectorAll('.hl-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            if (!currentSelectionInfo) return;
            const color = btn.dataset.color || 'Yellow';
            const colorClass = `hl-mark-${color.toLowerCase()}`;

            const sel = window.getSelection();
            if (sel && sel.rangeCount > 0) {
                const range = sel.getRangeAt(0);
                const mark = document.createElement('mark');
                mark.className = colorClass;
                mark.dataset.color = color;
                try {
                    range.surroundContents(mark);
                } catch (ex) {
                    const fragment = range.extractContents();
                    mark.appendChild(fragment);
                    range.insertNode(mark);
                }
            }

            notifyHost({
                action: 'add_highlight',
                recordKey: currentSelectionInfo.recordKey,
                field: currentSelectionInfo.field,
                text: currentSelectionInfo.text,
                color: color
            });

            sel?.removeAllRanges();
            hideToolbar();
            currentSelectionInfo = null;
        });
    });

    document.getElementById('btn-copy-selection')?.addEventListener('click', (e) => {
        e.stopPropagation();
        if (!currentSelectionInfo) return;
        navigator.clipboard.writeText(currentSelectionInfo.text);
        notifyHost({ action: 'copy_text', text: currentSelectionInfo.text });
        window.getSelection()?.removeAllRanges();
        hideToolbar();
        currentSelectionInfo = null;
    });

    document.getElementById('btn-note-selection')?.addEventListener('click', (e) => {
        e.stopPropagation();
        if (!currentSelectionInfo) return;
        notifyHost({
            action: 'add_note',
            recordKey: currentSelectionInfo.recordKey,
            text: currentSelectionInfo.text
        });
        window.getSelection()?.removeAllRanges();
        hideToolbar();
        currentSelectionInfo = null;
    });

    document.getElementById('btn-concordance')?.addEventListener('click', (e) => {
        e.stopPropagation();
        if (!currentSelectionInfo) return;
        notifyHost({
            action: 'concordance_lookup',
            word: currentSelectionInfo.text
        });
        window.getSelection()?.removeAllRanges();
        hideToolbar();
        currentSelectionInfo = null;
    });

    // Existing highlight click to remove or Sanskrit word click for concordance
    contentEl.addEventListener('click', (e) => {
        const mark = e.target.closest('mark[data-highlight-id]');
        if (mark && mark.dataset.highlightId) {
            const hlId = mark.dataset.highlightId;
            notifyHost({ action: 'remove_highlight', highlightId: hlId });
            const parent = mark.parentNode;
            if (parent) {
                parent.replaceChild(document.createTextNode(mark.textContent), mark);
                parent.normalize();
            }
            return;
        }

        const sanskritWord = e.target.closest('.sanskrit-word');
        if (sanskritWord) {
            e.stopPropagation();
            const wordText = sanskritWord.textContent.trim().replace(/[.,;—\-]/g, '');
            if (wordText) {
                let gloss = '';
                let next = sanskritWord.nextSibling;
                while (next && next.nodeType === Node.TEXT_NODE) {
                    let text = next.textContent || '';
                    let semiIdx = text.indexOf(';');
                    if (semiIdx >= 0) {
                        gloss += text.substring(0, semiIdx);
                        break;
                    } else {
                        gloss += text;
                    }
                    next = next.nextSibling;
                }
                gloss = gloss.replace(/^[\s—\-–:]+/, '').trim();
                showLexiconCard(sanskritWord, wordText, gloss);
            }
            return;
        }
    });

    // ---- Sanskrit Lemma Lexicon Popover Card ----
    let activeLexiconCard = null;

    function hideLexiconCard() {
        if (activeLexiconCard) {
            activeLexiconCard.remove();
            activeLexiconCard = null;
        }
    }

    function showLexiconCard(targetEl, wordText, gloss) {
        hideLexiconCard();
        const card = document.createElement('div');
        card.className = 'lexicon-popover-card';
        card.innerHTML = `
            <div class="lexicon-header">
                <span class="lexicon-word-term">${escapeHtml(wordText)}</span>
                <span class="lexicon-badge">Sanskrit Lemma</span>
            </div>
            <div class="lexicon-gloss">${gloss ? escapeHtml(gloss) : 'Sanskrit lemma in verse synonyms'}</div>
            <div class="lexicon-actions">
                <button class="btn-lexicon-explore" id="btn-lex-explore">🔍 Explore in all 40 Books</button>
                <button class="btn-lexicon-close" id="btn-lex-close">✕</button>
            </div>
        `;
        document.body.appendChild(card);
        activeLexiconCard = card;

        const rect = targetEl.getBoundingClientRect();
        const cardHeight = card.offsetHeight || 140;
        const cardWidth = card.offsetWidth || 320;
        const viewportHeight = window.innerHeight;
        const viewportWidth = window.innerWidth;

        const spaceBelow = viewportHeight - rect.bottom;
        const spaceAbove = rect.top;
        const gap = 8;

        let top;
        let placement = 'below';

        // Conscious adaptive positioning:
        // If not enough space below, present upwards;
        // if not enough space above, popup below.
        if (spaceBelow >= cardHeight + gap) {
            top = window.scrollY + rect.bottom + gap;
            placement = 'below';
        } else if (spaceAbove >= cardHeight + gap) {
            top = window.scrollY + rect.top - cardHeight - gap;
            placement = 'above';
        } else {
            // When space is constrained on both ends, choose the side with more available space
            if (spaceAbove > spaceBelow) {
                top = window.scrollY + rect.top - cardHeight - gap;
                placement = 'above';
            } else {
                top = window.scrollY + rect.bottom + gap;
                placement = 'below';
            }
            // Clamp within visible viewport margins
            const minTop = window.scrollY + 8;
            const maxTop = window.scrollY + viewportHeight - cardHeight - 8;
            top = Math.max(minTop, Math.min(top, maxTop));
        }

        // Horizontal alignment with boundary clamping
        let left = window.scrollX + rect.left;
        if (left + cardWidth > window.scrollX + viewportWidth - 16) {
            left = window.scrollX + viewportWidth - cardWidth - 16;
        }
        if (left < window.scrollX + 16) {
            left = window.scrollX + 16;
        }

        card.classList.add(`placement-${placement}`);
        card.style.top = `${Math.round(top)}px`;
        card.style.left = `${Math.round(left)}px`;

        card.querySelector('#btn-lex-explore')?.addEventListener('click', (e) => {
            e.stopPropagation();
            notifyHost({ action: 'concordance_lookup', word: wordText });
            hideLexiconCard();
        });

        card.querySelector('#btn-lex-close')?.addEventListener('click', (e) => {
            e.stopPropagation();
            hideLexiconCard();
        });
    }

    // Dismiss lexicon card when clicking outside or pressing Escape
    document.addEventListener('click', (e) => {
        if (activeLexiconCard && !activeLexiconCard.contains(e.target) && !e.target.closest('.sanskrit-word')) {
            hideLexiconCard();
        }
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && activeLexiconCard) {
            hideLexiconCard();
        }
    });

    // ---- Temple Recitation / Zen Mode ----
    let isZenModeActive = false;

    function setZenMode(enabled) {
        isZenModeActive = !!enabled;
        document.body.classList.remove('zen-recitation-mode');
        removeZenHud();
    }

    function ensureZenHud() {
        removeZenHud();
    }

    function removeZenHud() {
        document.getElementById('zen-hud')?.remove();
    }

    // Global keyboard shortcuts inside WebView2
    window.addEventListener('keydown', (e) => {
        // Ctrl+Left -> Previous verse/page
        if (e.ctrlKey && (e.key === 'ArrowLeft' || e.code === 'ArrowLeft')) {
            e.preventDefault();
            notifyHost({ action: 'shortcut_prev' });
            return;
        }
        // Ctrl+Right -> Next verse/page
        if (e.ctrlKey && (e.key === 'ArrowRight' || e.code === 'ArrowRight')) {
            e.preventDefault();
            notifyHost({ action: 'shortcut_next' });
            return;
        }
        // Ctrl + / Ctrl = -> Zoom in
        if (e.ctrlKey && (e.key === '=' || e.key === '+' || e.code === 'NumpadAdd' || e.code === 'Equal')) {
            e.preventDefault();
            notifyHost({ action: 'shortcut_zoom_in' });
            return;
        }
        // Ctrl - / Ctrl _ -> Zoom out
        if (e.ctrlKey && (e.key === '-' || e.key === '_' || e.code === 'NumpadSubtract' || e.code === 'Minus')) {
            e.preventDefault();
            notifyHost({ action: 'shortcut_zoom_out' });
            return;
        }
        // Ctrl 0 -> Zoom reset
        if (e.ctrlKey && (e.key === '0' || e.code === 'Digit0' || e.code === 'Numpad0')) {
            e.preventDefault();
            notifyHost({ action: 'shortcut_zoom_reset' });
            return;
        }
        // Escape -> Close find bar or exit focus mode
        if (e.key === 'Escape' || e.code === 'Escape') {
            notifyHost({ action: 'shortcut_escape' });
            if (isZenModeActive) {
                notifyHost({ action: 'exit_zen_mode' });
            }
        }
    });

    // Ctrl+MouseWheel Zoom
    window.addEventListener('wheel', (e) => {
        if (e.ctrlKey) {
            e.preventDefault();
            if (e.deltaY < 0) {
                notifyHost({ action: 'shortcut_zoom_in' });
            } else if (e.deltaY > 0) {
                notifyHost({ action: 'shortcut_zoom_out' });
            }
        }
    }, { passive: false });

    function onHashtagClick(tag) {
        notifyHost({ action: 'filter_hashtag', tag: tag });
    }

    function onWikiLinkClick(ref) {
        notifyHost({ action: 'navigate_reference', reference: ref });
    }

    // ---- In-Line Note & Realization Actions ----

    function openNoteEditor(recordKey) {
        const wrap = document.getElementById(`note-editor-wrap-${recordKey}`);
        if (!wrap) return;
        const noteIdInput = document.getElementById(`editor-note-id-${recordKey}`);
        const titleInput = document.getElementById(`editor-title-${recordKey}`);
        const bodyInput = document.getElementById(`editor-body-${recordKey}`);
        const label = document.getElementById(`editor-mode-label-${recordKey}`);

        if (noteIdInput) noteIdInput.value = '';
        if (titleInput) titleInput.value = '';
        if (bodyInput) bodyInput.innerHTML = '';
        if (label) label.textContent = 'New Realization';

        wrap.classList.remove('hidden');
        if (bodyInput) bodyInput.focus();
        wrap.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function closeNoteEditor(recordKey) {
        const wrap = document.getElementById(`note-editor-wrap-${recordKey}`);
        if (wrap) wrap.classList.add('hidden');
    }

    function editNote(recordKey, noteId) {
        const wrap = document.getElementById(`note-editor-wrap-${recordKey}`);
        const rawContentEl = document.getElementById(`raw-note-${noteId}`);
        const rawTitleEl = document.getElementById(`raw-note-title-${noteId}`);
        if (!wrap || !rawContentEl) return;

        const noteIdInput = document.getElementById(`editor-note-id-${recordKey}`);
        const titleInput = document.getElementById(`editor-title-${recordKey}`);
        const bodyInput = document.getElementById(`editor-body-${recordKey}`);
        const label = document.getElementById(`editor-mode-label-${recordKey}`);

        if (noteIdInput) noteIdInput.value = noteId;
        if (titleInput) titleInput.value = rawTitleEl ? rawTitleEl.value : '';
        if (bodyInput) bodyInput.innerHTML = rawContentEl.value;
        if (label) label.textContent = 'Edit Realization';

        wrap.classList.remove('hidden');
        if (bodyInput) bodyInput.focus();
        wrap.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    function sanitizeNoteHtml(html) {
        if (!html) return '';
        // Replace &nbsp; (browser injects these in contenteditable on Space/Enter)
        // with a regular space so they don't show as literal "&nbsp;" when re-rendered
        return html
            .replace(/&nbsp;/gi, ' ')
            .replace(/&amp;nbsp;/gi, ' ')
            // Collapse multiple consecutive spaces from &nbsp; runs
            .replace(/ {2,}/g, ' ')
            .trim();
    }

    function saveNote(recordKey) {
        const noteIdInput = document.getElementById(`editor-note-id-${recordKey}`);
        const titleInput = document.getElementById(`editor-title-${recordKey}`);
        const bodyInput = document.getElementById(`editor-body-${recordKey}`);

        const noteId = noteIdInput ? noteIdInput.value.trim() : '';
        const title = titleInput ? titleInput.value.trim() : '';
        const rawHtml = bodyInput ? bodyInput.innerHTML.trim() : '';
        const content = sanitizeNoteHtml(rawHtml);

        if (!content || content === '<br>') {
            alert('Please enter your realization or note before saving.');
            return;
        }

        notifyHost({
            action: 'save_note',
            recordKey: recordKey,
            noteId: noteId,
            title: title,
            content: content
        });
    }

    function deleteNote(recordKey, noteId) {
        if (!confirm('Are you sure you want to delete this realization?')) return;
        notifyHost({
            action: 'delete_note',
            recordKey: recordKey,
            noteId: noteId
        });
    }

    function onNoteSaved(note) {
        if (!note) return;
        const recordKey = note.recordKey || note.RecordKey;
        if (!recordKey) return;

        const section = document.getElementById(`notes-section-${recordKey}`);
        if (!section) return;
        const verseRef = section.dataset.verseRef || recordKey;

        // Hide editor
        closeNoteEditor(recordKey);

        // Remove empty state if present
        const emptyEl = document.getElementById(`notes-empty-${recordKey}`);
        if (emptyEl) emptyEl.remove();

        let listEl = document.getElementById(`notes-list-${recordKey}`);
        if (!listEl) {
            listEl = document.createElement('div');
            listEl.className = 'notes-list';
            listEl.id = `notes-list-${recordKey}`;
            const header = section.querySelector('.notes-section-header');
            if (header && header.nextSibling) {
                section.insertBefore(listEl, header.nextSibling);
            } else {
                section.appendChild(listEl);
            }
        }

        const noteId = note.id || note.Id;
        const existingCard = document.getElementById(`note-card-${noteId}`);
        const cardHtml = renderSingleNoteCardHtml(note, recordKey, verseRef);

        if (existingCard) {
            const temp = document.createElement('div');
            temp.innerHTML = cardHtml;
            existingCard.replaceWith(temp.firstElementChild);
        } else {
            const temp = document.createElement('div');
            temp.innerHTML = cardHtml;
            listEl.appendChild(temp.firstElementChild);
        }

        // Update count badge
        const countBadge = document.getElementById(`notes-count-${recordKey}`);
        if (countBadge) {
            const count = listEl.querySelectorAll('.note-card').length;
            countBadge.textContent = String(count);
        }
    }

    function onNoteDeleted(noteId, recordKey) {
        const card = document.getElementById(`note-card-${noteId}`);
        if (card) {
            card.remove();
        }

        const listEl = document.getElementById(`notes-list-${recordKey}`);
        const count = listEl ? listEl.querySelectorAll('.note-card').length : 0;

        const countBadge = document.getElementById(`notes-count-${recordKey}`);
        if (countBadge) countBadge.textContent = String(count);

        if (count === 0 && listEl) {
            const section = document.getElementById(`notes-section-${recordKey}`);
            if (section) {
                const emptyEl = document.createElement('div');
                emptyEl.className = 'notes-empty-state';
                emptyEl.id = `notes-empty-${recordKey}`;
                emptyEl.textContent = 'No personal realizations added for this verse yet. Click "+ Add Realization" to record your notes.';
                listEl.replaceWith(emptyEl);
            }
        }
    }

    function exportSingleNote(noteId, verseReference) {
        const rawContentEl = document.getElementById(`raw-note-${noteId}`);
        const rawTitleEl = document.getElementById(`raw-note-title-${noteId}`);
        const content = rawContentEl ? rawContentEl.value : '';
        const title = rawTitleEl ? rawTitleEl.value : '';

        // Generate clean markdown text
        let md = `# Realization on ${verseReference}\n`;
        if (title) md += `## ${title}\n\n`;
        md += `*Exported on ${new Date().toLocaleDateString()}*\n\n---\n\n`;
        let cleanText = content
            .replace(/<br\s*[\/]?>/gi, '\n')
            .replace(/<\/p>/gi, '\n\n')
            .replace(/<p>/gi, '')
            .replace(/<strong>(.*?)<\/strong>/gi, '**$1**')
            .replace(/<b>(.*?)<\/b>/gi, '**$1**')
            .replace(/<em>(.*?)<\/em>/gi, '*$1*')
            .replace(/<i>(.*?)<\/i>/gi, '*$1*')
            .replace(/<u>(.*?)<\/u>/gi, '__$1__')
            .replace(/<mark[^>]*>(.*?)<\/mark>/gi, '==$1==')
            .replace(/<a[^>]*href="([^"]*)"[^>]*>(.*?)<\/a>/gi, '[$2]($1)')
            .replace(/<li>(.*?)<\/li>/gi, '- $1\n')
            .replace(/<\/?[^>]+(>|$)/g, '');
        md += cleanText;

        notifyHost({
            action: 'export_note',
            reference: verseReference,
            content: md
        });
    }

    function exportDraft(recordKey, verseReference) {
        const titleInput = document.getElementById(`editor-title-${recordKey}`);
        const bodyInput = document.getElementById(`editor-body-${recordKey}`);
        const title = titleInput ? titleInput.value : '';
        const content = bodyInput ? bodyInput.innerText : '';

        let md = `# Realization on ${verseReference}\n`;
        if (title) md += `## ${title}\n\n`;
        md += `*Exported draft on ${new Date().toLocaleDateString()}*\n\n---\n\n${content}`;

        notifyHost({
            action: 'export_note',
            reference: verseReference,
            content: md
        });
    }

    // Rich Formatting Execs & Custom Table Creator
    let savedEditorRange = null;

    function saveSelectionRange(recordKey) {
        const body = document.getElementById(`editor-body-${recordKey}`);
        const sel = window.getSelection();
        if (sel && sel.rangeCount > 0) {
            const range = sel.getRangeAt(0);
            if (body && body.contains(range.commonAncestorContainer)) {
                savedEditorRange = range.cloneRange();
                return;
            }
        }
        savedEditorRange = null;
    }

    function restoreSelectionRange(recordKey) {
        const body = document.getElementById(`editor-body-${recordKey}`);
        if (!body) return;
        body.focus();
        if (savedEditorRange) {
            try {
                const sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(savedEditorRange);
                return;
            } catch (e) {
                // Ignore range failure
            }
        }
    }

    function formatBlock(recordKey, tag) {
        if (!tag) return;
        const body = document.getElementById(`editor-body-${recordKey}`);
        if (body) body.focus();
        const blockTag = tag.startsWith('<') ? tag : `<${tag}>`;
        document.execCommand('formatBlock', false, blockTag);
    }

    function execFormat(cmd, val = null) {
        document.execCommand(cmd, false, val);
    }

    function execHighlight() {
        const sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) {
            alert('Please select the text in your note that you wish to highlight.');
            return;
        }
        document.execCommand('hiliteColor', false, '#facc15');
    }

    function generateGridCellsHtml(rk, maxRows, maxCols) {
        let cells = '';
        for (let r = 1; r <= maxRows; r++) {
            for (let c = 1; c <= maxCols; c++) {
                cells += `<div class="grid-cell" data-r="${r}" data-c="${c}" onmouseenter="reader.onCellHover('${escapeHtml(rk)}', ${r}, ${c})" onclick="reader.onCellClick('${escapeHtml(rk)}', ${r}, ${c})"></div>`;
            }
        }
        return cells;
    }

    function toggleTableDialog(recordKey) {
        const popover = document.getElementById(`table-popover-${recordKey}`);
        if (!popover) return;
        const isHidden = popover.classList.contains('hidden');

        // Close any other open table popovers
        document.querySelectorAll('.table-popover').forEach(p => p.classList.add('hidden'));

        if (isHidden) {
            saveSelectionRange(recordKey);
            popover.classList.remove('hidden');
            highlightGridCells(recordKey, 3, 3);
            const label = document.getElementById(`table-grid-label-${recordKey}`);
            if (label) label.textContent = '3 Columns × 3 Rows';
            const rowsInput = document.getElementById(`table-rows-${recordKey}`);
            const colsInput = document.getElementById(`table-cols-${recordKey}`);
            if (rowsInput) rowsInput.value = 3;
            if (colsInput) colsInput.value = 3;
        } else {
            popover.classList.add('hidden');
        }
    }

    function closeTableDialog(recordKey) {
        const popover = document.getElementById(`table-popover-${recordKey}`);
        if (popover) popover.classList.add('hidden');
    }

    function onCellHover(recordKey, r, c) {
        highlightGridCells(recordKey, r, c);
        const label = document.getElementById(`table-grid-label-${recordKey}`);
        if (label) label.textContent = `${c} Columns × ${r} Rows`;
        const rowsInput = document.getElementById(`table-rows-${recordKey}`);
        const colsInput = document.getElementById(`table-cols-${recordKey}`);
        if (rowsInput) rowsInput.value = r;
        if (colsInput) colsInput.value = c;
    }

    function onGridMouseLeave(recordKey) {
        const rowsInput = document.getElementById(`table-rows-${recordKey}`);
        const colsInput = document.getElementById(`table-cols-${recordKey}`);
        const r = parseInt(rowsInput?.value || '3', 10);
        const c = parseInt(colsInput?.value || '3', 10);
        highlightGridCells(recordKey, r, c);
        const label = document.getElementById(`table-grid-label-${recordKey}`);
        if (label) label.textContent = `${c} Columns × ${r} Rows`;
    }

    function onTableInputChanged(recordKey) {
        const rowsInput = document.getElementById(`table-rows-${recordKey}`);
        const colsInput = document.getElementById(`table-cols-${recordKey}`);
        const r = Math.max(1, Math.min(30, parseInt(rowsInput?.value || '1', 10)));
        const c = Math.max(1, Math.min(10, parseInt(colsInput?.value || '1', 10)));
        highlightGridCells(recordKey, r, c);
        const label = document.getElementById(`table-grid-label-${recordKey}`);
        if (label) label.textContent = `${c} Columns × ${r} Rows`;
    }

    function highlightGridCells(recordKey, r, c) {
        const matrix = document.getElementById(`table-grid-matrix-${recordKey}`);
        if (!matrix) return;
        const cells = matrix.querySelectorAll('.grid-cell');
        cells.forEach(cell => {
            const cellR = parseInt(cell.dataset.r, 10);
            const cellC = parseInt(cell.dataset.c, 10);
            if (cellR <= r && cellC <= c) {
                cell.classList.add('cell-active');
            } else {
                cell.classList.remove('cell-active');
            }
        });
    }

    function onCellClick(recordKey, r, c) {
        insertCustomTable(recordKey, r, c);
    }

    function insertPresetTable(recordKey, r, c) {
        insertCustomTable(recordKey, r, c);
    }

    function insertCustomTable(recordKey, explicitR = null, explicitC = null) {
        const rowsInput = document.getElementById(`table-rows-${recordKey}`);
        const colsInput = document.getElementById(`table-cols-${recordKey}`);
        const headerCheck = document.getElementById(`table-header-${recordKey}`);
        const bodyInput = document.getElementById(`editor-body-${recordKey}`);

        const r = explicitR !== null ? explicitR : Math.max(1, Math.min(30, parseInt(rowsInput?.value || '3', 10)));
        const c = explicitC !== null ? explicitC : Math.max(1, Math.min(10, parseInt(colsInput?.value || '3', 10)));
        const hasHeader = headerCheck ? headerCheck.checked : true;

        closeTableDialog(recordKey);

        if (!bodyInput) return;
        restoreSelectionRange(recordKey);

        let tableHtml = '<table border="1" class="realization-table">';
        if (hasHeader) {
            tableHtml += '<thead><tr>';
            for (let col = 1; col <= c; col++) {
                tableHtml += `<th>Header ${col}</th>`;
            }
            tableHtml += '</tr></thead>';
        }
        tableHtml += '<tbody>';
        for (let row = 1; row <= r; row++) {
            tableHtml += '<tr>';
            for (let col = 1; col <= c; col++) {
                tableHtml += `<td>Cell</td>`;
            }
            tableHtml += '</tr>';
        }
        tableHtml += '</tbody></table><p><br></p>';

        document.execCommand('insertHTML', false, tableHtml);
        savedEditorRange = null;
    }

    function insertTable(recordKey) {
        toggleTableDialog(recordKey);
    }

    function toggleLinkDialog(recordKey) {
        const popover = document.getElementById(`link-popover-${recordKey}`);
        if (!popover) return;
        const isHidden = popover.classList.contains('hidden');

        // Close any other open popovers
        document.querySelectorAll('.table-popover, .link-popover').forEach(p => p.classList.add('hidden'));

        if (isHidden) {
            saveSelectionRange(recordKey);
            const sel = window.getSelection();
            const selText = sel ? sel.toString().trim() : '';

            const textInput = document.getElementById(`link-text-${recordKey}`);
            const urlInput = document.getElementById(`link-url-${recordKey}`);

            if (textInput) textInput.value = selText;
            if (urlInput && !urlInput.value) urlInput.value = '';

            popover.classList.remove('hidden');

            setTimeout(() => {
                if (selText && urlInput) {
                    urlInput.focus();
                    urlInput.select();
                } else if (textInput) {
                    textInput.focus();
                    textInput.select();
                }
            }, 50);
        } else {
            popover.classList.add('hidden');
        }
    }

    function closeLinkDialog(recordKey) {
        const popover = document.getElementById(`link-popover-${recordKey}`);
        if (popover) popover.classList.add('hidden');
    }

    function insertCustomLink(recordKey) {
        const textInput = document.getElementById(`link-text-${recordKey}`);
        const urlInput = document.getElementById(`link-url-${recordKey}`);
        const bodyInput = document.getElementById(`editor-body-${recordKey}`);

        let url = urlInput ? urlInput.value.trim() : '';
        let title = textInput ? textInput.value.trim() : '';

        if (!url || url === 'https://' || url === 'http://') {
            alert('Please enter a destination web URL (e.g. prabhupadavani.com)');
            if (urlInput) urlInput.focus();
            return;
        }

        if (!/^https?:\/\//i.test(url)) {
            url = 'https://' + url;
        }

        if (!title) {
            try {
                title = new URL(url).hostname || url;
            } catch (e) {
                title = url;
            }
        }

        closeLinkDialog(recordKey);

        if (!bodyInput) return;
        restoreSelectionRange(recordKey);

        const linkHtml = `<a href="${escapeHtml(url)}" class="external-link in-app-link" data-url="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer">${escapeHtml(title)} ↗</a> `;
        document.execCommand('insertHTML', false, linkHtml);
        savedEditorRange = null;
    }

    function promptLink(recordKey) {
        toggleLinkDialog(recordKey);
    }

    function insertAtPrompt(recordKey) {
        const body = document.getElementById(`editor-body-${recordKey}`);
        if (body) body.focus();
        const ref = prompt('Enter scripture reference (e.g. BG 4.9, SB 1.2.6, CC Madhya 20.108, ISO 1):');
        if (!ref) return;
        const cleanRef = ref.trim().replace(/^@/, '');
        if (!cleanRef) return;
        const chip = `<a class="scripture-link at-mention" href="#" data-ref="${escapeHtml(cleanRef)}" onclick="reader.onNavigateScripture(event, '${escapeHtml(cleanRef)}')">@${escapeHtml(cleanRef)}</a> `;
        document.execCommand('insertHTML', false, chip);
    }

    function insertCode(recordKey) {
        const body = document.getElementById(`editor-body-${recordKey}`);
        if (body) body.focus();
        const sel = window.getSelection();
        const text = sel ? sel.toString().trim() : '';
        if (text) {
            document.execCommand('insertHTML', false, `<code class="note-inline-code">${escapeHtml(text)}</code>`);
        } else {
            document.execCommand('insertHTML', false, `<code class="note-inline-code">code</code> `);
        }
    }

    function handleEditorInput(e, recordKey) {
        if (!e) return;
        // Live Markdown syntax conversion when Space is typed
        if (e.inputType === 'insertText' && e.data === ' ') {
            const sel = window.getSelection();
            if (!sel || !sel.isCollapsed) return;
            const node = sel.anchorNode;
            if (!node || node.nodeType !== Node.TEXT_NODE) return;

            const text = node.textContent;
            const offset = sel.anchorOffset;
            const before = text.substring(0, offset);

            if (before === '# ') {
                node.textContent = text.substring(offset);
                formatBlock(recordKey, 'h1');
            } else if (before === '## ') {
                node.textContent = text.substring(offset);
                formatBlock(recordKey, 'h2');
            } else if (before === '### ') {
                node.textContent = text.substring(offset);
                formatBlock(recordKey, 'h3');
            } else if (before === '- ' || before === '* ') {
                node.textContent = text.substring(offset);
                execFormat('insertUnorderedList');
            } else if (before === '1. ') {
                node.textContent = text.substring(offset);
                execFormat('insertOrderedList');
            } else if (before === '> ') {
                node.textContent = text.substring(offset);
                formatBlock(recordKey, 'blockquote');
            }
        }
    }

    // Dismiss popovers when clicking anywhere outside
    document.addEventListener('click', (e) => {
        if (!e.target.closest('.table-popover') && !e.target.closest('.tb-table-btn')) {
            document.querySelectorAll('.table-popover').forEach(p => p.classList.add('hidden'));
        }
        if (!e.target.closest('.link-popover') && !e.target.closest('.tb-link-btn')) {
            document.querySelectorAll('.link-popover').forEach(p => p.classList.add('hidden'));
        }
    });

    // Intercept clicks on web links to open as in-app tabs
    document.addEventListener('click', (e) => {
        const link = e.target.closest('a.external-link, a.in-app-link');
        if (link) {
            e.preventDefault();
            e.stopPropagation();
            const url = link.getAttribute('href') || link.dataset.url || '';
            const rawText = link.textContent || '';
            const title = rawText.replace(/[↗\s]+$/, '').trim() || url;
            if (url && url !== '#') {
                notifyHost({
                    action: 'open_web_tab',
                    url: url,
                    title: title
                });
            }
        }
    });

    // ---- Keyboard Shortcuts Inside WebView2 ----
    window.addEventListener('keydown', (e) => {
        const key = e.key.toLowerCase();
        const activeEl = document.activeElement;
        const isEditor = activeEl && (activeEl.classList.contains('editor-content') || activeEl.closest('.editor-content'));

        if (isEditor) {
            const editorBody = activeEl.classList.contains('editor-content') ? activeEl : activeEl.closest('.editor-content');
            const rk = editorBody ? editorBody.id.replace('editor-body-', '') : '';

            // Tab navigation in tables
            if (e.key === 'Tab') {
                const sel = window.getSelection();
                let container = sel ? sel.anchorNode : null;
                if (container && container.nodeType === Node.TEXT_NODE) container = container.parentNode;
                const cell = container ? container.closest('td, th') : null;
                if (cell) {
                    e.preventDefault();
                    const table = cell.closest('table');
                    const allCells = Array.from(table.querySelectorAll('th, td'));
                    const idx = allCells.indexOf(cell);
                    if (!e.shiftKey) {
                        if (idx < allCells.length - 1) {
                            allCells[idx + 1].focus();
                        } else {
                            // Last cell: Append a new row!
                            const tbody = table.querySelector('tbody') || table;
                            const firstRow = table.querySelector('tr');
                            const colCount = firstRow ? firstRow.children.length : 2;
                            const newTr = document.createElement('tr');
                            for (let c = 0; c < colCount; c++) {
                                const newTd = document.createElement('td');
                                newTd.textContent = 'Cell';
                                newTr.appendChild(newTd);
                            }
                            tbody.appendChild(newTr);
                            newTr.children[0].focus();
                        }
                    } else {
                        if (idx > 0) {
                            allCells[idx - 1].focus();
                        }
                    }
                    return;
                }
            }

            // Ctrl+K: Hyperlink dialog
            if ((e.ctrlKey || e.metaKey) && key === 'k') {
                e.preventDefault();
                toggleLinkDialog(rk);
                return;
            }

            // Ctrl+Shift+X or Ctrl+Shift+S: Strikethrough
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && (key === 'x' || key === 's')) {
                e.preventDefault();
                execFormat('strikeThrough');
                return;
            }

            // Ctrl+Shift+H: Highlight
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && key === 'h') {
                e.preventDefault();
                execHighlight();
                return;
            }

            // Ctrl+Shift+C: Inline Code
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && key === 'c') {
                e.preventDefault();
                insertCode(rk);
                return;
            }

            // Ctrl+Shift+7 or Ctrl+Shift+O: Numbered List
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && (key === '7' || key === 'o')) {
                e.preventDefault();
                execFormat('insertOrderedList');
                return;
            }

            // Ctrl+Shift+8 or Ctrl+Shift+U: Bullet List
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && (key === '8' || key === 'u')) {
                e.preventDefault();
                execFormat('insertUnorderedList');
                return;
            }

            // Ctrl+Shift+Q or Ctrl+Shift+.: Blockquote
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && (key === 'q' || key === '.' || key === '>')) {
                e.preventDefault();
                formatBlock(rk, 'blockquote');
                return;
            }

            // Ctrl+Alt+T or Ctrl+Shift+T: Table Dialog
            if ((e.ctrlKey || e.metaKey) && (e.altKey || e.shiftKey) && key === 't') {
                e.preventDefault();
                toggleTableDialog(rk);
                return;
            }

            // Headings: Ctrl+Alt+1, Ctrl+Alt+2, Ctrl+Alt+3, Ctrl+Alt+0
            if ((e.ctrlKey || e.metaKey) && e.altKey && key === '1') {
                e.preventDefault();
                formatBlock(rk, 'h1');
                return;
            }
            if ((e.ctrlKey || e.metaKey) && e.altKey && key === '2') {
                e.preventDefault();
                formatBlock(rk, 'h2');
                return;
            }
            if ((e.ctrlKey || e.metaKey) && e.altKey && key === '3') {
                e.preventDefault();
                formatBlock(rk, 'h3');
                return;
            }
            if ((e.ctrlKey || e.metaKey) && e.altKey && key === '0') {
                e.preventDefault();
                formatBlock(rk, 'p');
                return;
            }
        }

        if (isZenModeActive) {
            if (e.key === 'ArrowLeft') {
                e.preventDefault();
                notifyHost({ action: 'shortcut_prev' });
                return;
            } else if (e.key === 'ArrowRight') {
                e.preventDefault();
                notifyHost({ action: 'shortcut_next' });
                return;
            } else if (e.key === 'Escape') {
                e.preventDefault();
                setZenMode(false);
                notifyHost({ action: 'exit_zen_mode' });
                return;
            }
        }

        if (e.key === 'F3') {
            e.preventDefault();
            if (e.shiftKey) {
                prevHit();
            } else {
                nextHit();
            }
            return;
        }

        if ((e.ctrlKey || e.metaKey) && key === 'f') {
            e.preventDefault();
            notifyHost({ action: 'shortcut_find' });
        } else if ((e.ctrlKey || e.metaKey) && e.shiftKey && key === 'l') {
            e.preventDefault();
            notifyHost({ action: 'shortcut_theme' });
        } else if (e.altKey && e.key === 'ArrowLeft') {
            e.preventDefault();
            notifyHost({ action: 'shortcut_prev' });
        } else if (e.altKey && e.key === 'ArrowRight') {
            e.preventDefault();
            notifyHost({ action: 'shortcut_next' });
        } else if (e.key === 'Escape') {
            clearSearchHits();
            stopChantingPulse();
            hideToolbar();
            document.querySelectorAll('.table-popover, .link-popover').forEach(p => p.classList.add('hidden'));
            notifyHost({ action: 'shortcut_escape' });
        }
    });

    // ---- Pillar 5: Hit-to-Hit In-Reader Navigation Engine ----
    let searchHitElements = [];
    let currentHitIndex = -1;

    function ensureHitNavHud() {
        let hud = document.getElementById('hit-navigation-hud');
        if (!hud) {
            hud = document.createElement('div');
            hud.id = 'hit-navigation-hud';
            hud.innerHTML = `
                <button id="hit-prev-btn" title="Previous Hit (Shift+F3)">‹</button>
                <span id="hit-counter-text">Hit 0 of 0</span>
                <button id="hit-next-btn" title="Next Hit (F3)">›</button>
                <button id="hit-close-btn" title="Dismiss Search Hits (Esc)">✕</button>
            `;
            document.body.appendChild(hud);

            document.getElementById('hit-prev-btn').addEventListener('click', prevHit);
            document.getElementById('hit-next-btn').addEventListener('click', nextHit);
            document.getElementById('hit-close-btn').addEventListener('click', clearSearchHits);
        }
        return hud;
    }

    function clearSearchHits() {
        searchHitElements = [];
        currentHitIndex = -1;
        const hud = document.getElementById('hit-navigation-hud');
        if (hud) hud.style.display = 'none';

        document.querySelectorAll('mark.search-hit').forEach(mark => {
            const parent = mark.parentNode;
            if (parent) {
                parent.replaceChild(document.createTextNode(mark.textContent), mark);
                parent.normalize();
            }
        });
    }

    function highlightSearchTerms(rawQuery) {
        clearSearchHits();
        if (!rawQuery || typeof rawQuery !== 'string') return;

        // Extract individual search words (strip operators and punctuation)
        const cleaned = rawQuery.replace(/NEAR\([^)]+\)/gi, '')
                                .replace(/\b(AND|OR|NOT|w\/\d+|near\/\d+)\b/gi, ' ')
                                .replace(/["'():]/g, ' ');
        const words = cleaned.split(/\s+/).map(w => w.trim()).filter(w => w.length >= 2);
        if (words.length === 0) return;

        // Escape regex special chars
        const escaped = words.map(w => w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
        const pattern = new RegExp(`(${escaped.join('|')})`, 'gi');

        const walker = document.createTreeWalker(
            contentEl,
            NodeFilter.SHOW_TEXT,
            {
                acceptNode: function(node) {
                    if (!node.nodeValue.trim()) return NodeFilter.FILTER_REJECT;
                    const parent = node.parentElement;
                    if (!parent) return NodeFilter.FILTER_REJECT;
                    const tag = parent.tagName.toLowerCase();
                    if (tag === 'script' || tag === 'style' || tag === 'mark' || tag === 'button') {
                        return NodeFilter.FILTER_REJECT;
                    }
                    if (parent.closest('#selection-toolbar, #lexicon-popover, #hit-navigation-hud')) {
                        return NodeFilter.FILTER_REJECT;
                    }
                    return NodeFilter.FILTER_ACCEPT;
                }
            }
        );

        const nodesToProcess = [];
        while (walker.nextNode()) {
            if (pattern.test(walker.currentNode.nodeValue)) {
                nodesToProcess.push(walker.currentNode);
            }
        }

        nodesToProcess.forEach(node => {
            const val = node.nodeValue;
            const parent = node.parentNode;
            if (!parent) return;

            const fragment = document.createDocumentFragment();
            let lastIdx = 0;
            pattern.lastIndex = 0;
            let match;

            while ((match = pattern.exec(val)) !== null) {
                if (match.index > lastIdx) {
                    fragment.appendChild(document.createTextNode(val.substring(lastIdx, match.index)));
                }
                const mark = document.createElement('mark');
                mark.className = 'search-hit';
                mark.textContent = match[0];
                fragment.appendChild(mark);
                searchHitElements.push(mark);
                lastIdx = pattern.lastIndex;
            }

            if (lastIdx < val.length) {
                fragment.appendChild(document.createTextNode(val.substring(lastIdx)));
            }

            parent.replaceChild(fragment, node);
        });

        if (searchHitElements.length > 0) {
            const hud = ensureHitNavHud();
            hud.style.display = 'flex';
            currentHitIndex = 0;
            updateActiveHit(true);
        }
    }

    function updateActiveHit(scrollIntoView = true) {
        if (searchHitElements.length === 0) return;
        searchHitElements.forEach((el, idx) => {
            if (idx === currentHitIndex) {
                el.classList.add('active-hit');
                if (scrollIntoView) {
                    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
                }
            } else {
                el.classList.remove('active-hit');
            }
        });

        const counter = document.getElementById('hit-counter-text');
        if (counter) {
            counter.textContent = `Hit ${currentHitIndex + 1} of ${searchHitElements.length}`;
        }
    }

    function nextHit() {
        if (searchHitElements.length === 0) return;
        currentHitIndex = (currentHitIndex + 1) % searchHitElements.length;
        updateActiveHit(true);
    }

    function prevHit() {
        if (searchHitElements.length === 0) return;
        currentHitIndex = (currentHitIndex - 1 + searchHitElements.length) % searchHitElements.length;
        updateActiveHit(true);
    }

    window.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' || e.keyCode === 27) {
            window.chrome?.webview?.postMessage({ type: 'exit_zen_mode' });
        }
    });

})();

