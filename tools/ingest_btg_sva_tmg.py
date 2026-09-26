import re
import os
import sys
import sqlite3
import shutil
import json
from datetime import datetime

sys.stdout.reconfigure(encoding='utf-8')

BALARAM_MAP = {
    r"\'e4": 'ā', r"\'c4": 'Ā',
    r"\'e5": 'ṛ', r"\'c5": 'Ṛ',
    r"\'e7": 'ś', r"\'c7": 'Ś',
    r"\'e9": 'ī', r"\'c9": 'Ī',
    r"\'eb": 'ṇ', r"\'cb": 'Ṇ',
    r"\'ef": 'ñ', r"\'cf": 'Ñ',
    r"\'f1": 'ṣ', r"\'d1": 'Ṣ',
    r"\'f2": 'ḍ', r"\'d2": 'Ḍ',
    r"\'f6": 'ṭ', r"\'d6": 'Ṭ',
    r"\'f9": 'ḥ', r"\'d9": 'Ḥ',
    r"\'fc": 'ū', r"\'dc": 'Ū',
    r"\'e0": 'ṁ', r"\'c0": 'Ṁ',
    r"\'e1": 'ṁ',
    r"\'e6": 'ṝ', r"\'c6": 'Ṝ',
    r"\'ec": 'ṅ', r"\'cc": 'Ṅ',
    r"\'ee": 'ḷ', r"\'ce": 'Ḷ',
    r"\'97": '—',
    r"\'96": '–',
    r"\'91": '‘', r"\'92": '’',
    r"\'93": '"', r"\'94": '"',
}

KNOWN_TITLE_FIXES = {
    "Sri Sri Gurv-astakaŚrī Śrī Gurv-aṣṭaka": "Śrī Śrī Gurv-aṣṭaka",
    "Arunodaya-kirtana I Audilo ArunaSVA 2: Aruṇodaya-kīrtana I": "Aruṇodaya-kīrtana I",
    "Arunodaya-kirtana I Audilo ArunaSVA 2: Aruṇodaya-kīrtana II": "Aruṇodaya-kīrtana II",
    "Vidyara VilaseVidyāra Vilāse": "Vidyāra Vilāse",
}

def decode_text(t):
    if not t:
        return ""
    for k, v in BALARAM_MAP.items():
        t = t.replace(k, v)
    t = re.sub(r'\\pard\b[^\s\\{}]*', '', t)
    t = re.sub(r'\\\*[a-zA-Z]+(?:\d+)?', '', t)
    t = re.sub(r'\\[a-zA-Z]+(?:-?[0-9]+)?\s?', '', t)
    t = re.sub(r"\\'[0-9a-fA-F]{2}", '', t)
    t = t.replace('{', '').replace('}', '').strip()
    return re.sub(r'\s+', ' ', t).strip()

def clean_rtf_block(rtf_text):
    for k, v in BALARAM_MAP.items():
        rtf_text = rtf_text.replace(k, v)
    rtf_text = re.sub(r'([a-zA-ZāīūṛṝḷñṅṇṭḍśṣṁṃḥĀĪŪṚṜḶÑṄṆṬḌŚṢṀṂḤ])\r?\n([a-zA-ZāīūṛṝḷñṅṇṭḍśṣṁṃḥĀĪŪṚṜḶÑṄṆṬḌŚṢṀṂḤ])', r'\1\2', rtf_text)
    rtf_text = rtf_text.replace(r'\line', ' ')
    rtf_text = re.sub(r'\\pard\b[^\s\\{}]*', '', rtf_text)
    paras = re.split(r'\\par(?![a-zA-Z])', rtf_text)
    cleaned = []
    for p in paras:
        p = re.sub(r'\\\*[a-zA-Z]+(?:\d+)?', '', p)
        p = re.sub(r'\\[a-zA-Z]+(?:-?[0-9]+)?\s?', '', p)
        p = re.sub(r"\\'[0-9a-fA-F]{2}", '', p)
        p = p.replace('{', '').replace('}', '').strip()
        p = re.sub(r'\s+', ' ', p)
        p = re.sub(r'^[a-zA-Z]\s+(?=[A-Z\"])', '', p)
        if len(p) > 15:
            cleaned.append(p)
    return cleaned

def clean_title_string(raw):
    t = decode_text(raw)
    if '*' in t:
        t = t.split('*')[-1].strip()
    for k, v in KNOWN_TITLE_FIXES.items():
        if k in t:
            t = t.replace(k, v)
    t = re.sub(r'^SVA(?:\s+\d+)?:?\s*', '', t).strip()
    t = re.sub(r'^\*?[A-Za-z0-9\s\-_:\'\(\)]+\*+(?=[A-ZŚ])', '', t).strip()
    return t

def parse_sva_song_chunk(chunk):
    raw_paras = re.split(r'\\par(?![a-zA-Z])', chunk)
    banner = ""
    subtitle = ""
    stanzas = []
    current_stanza = None
    state = 'INIT'
    purport_paras = []

    for p in raw_paras:
        sm = re.search(r'\\s(\d+)', p)
        style = int(sm.group(1)) if sm else -1
        txt = decode_text(p)
        if not txt:
            continue

        if style == 489:
            pts = [decode_text(x) for x in p.split(r'\line') if decode_text(x)]
            if pts:
                banner = pts[0]
                if '*' in banner:
                    banner = banner.split('*')[-1].strip()
                if len(pts) > 1:
                    subtitle = pts[1]
            else:
                banner = txt
            continue

        if 'Audio' in txt and len(txt) < 15:
            continue

        is_text_label = bool(re.match(r'^Text\s+(?:One|Two|Three|Four|Five|Six|Seven|Eight|Nine|Ten|Eleven|Twelve|Thirteen|Fourteen|Fifteen|Sixteen|Seventeen|Eighteen|Nineteen|Twenty|\d+)', txt, re.IGNORECASE))
        is_syn_hdr = txt.strip().upper() == 'SYNONYMS'
        is_trans_hdr = txt.strip().upper() == 'TRANSLATION'
        is_purport_hdr = txt.strip().upper() == 'PURPORT' or bool(re.search(r'Purport\s+by\s+His\s+Divine\s+Grace', txt, re.IGNORECASE))

        if is_purport_hdr or (style in (181, 1456, 1709) and 'purport' in txt.lower() and len(txt) < 80):
            state = 'PURPORT'
            if current_stanza:
                stanzas.append(current_stanza)
                current_stanza = None
            continue

        if state == 'PURPORT':
            clean_p = re.sub(r'^\*?[A-Za-z0-9\s\-_:\'\(\)]+\*+(?=[A-ZŚ])', '', txt)
            purport_paras.append(clean_p)
            continue

        if is_text_label:
            if current_stanza:
                stanzas.append(current_stanza)
            current_stanza = {
                'label': txt,
                'lines': [],
                'synonyms': '',
                'translation': ''
            }
            state = 'LINES'
            continue

        if is_syn_hdr:
            state = 'SYNONYMS'
            continue

        if is_trans_hdr:
            state = 'TRANSLATION'
            continue

        if style == 9:
            if current_stanza and current_stanza.get('translation'):
                stanzas.append(current_stanza)
                current_stanza = None
            if current_stanza is None:
                current_stanza = {'label': '', 'lines': [], 'synonyms': '', 'translation': ''}
                state = 'LINES'
            lines = [decode_text(x) for x in p.split(r'\line') if decode_text(x)]
            current_stanza['lines'].extend(lines)
            continue

        if state == 'SYNONYMS' or style == 1962:
            if current_stanza is None:
                current_stanza = {'label': '', 'lines': [], 'synonyms': '', 'translation': ''}
            if current_stanza['synonyms']:
                current_stanza['synonyms'] += ' ' + txt
            else:
                current_stanza['synonyms'] = txt
            continue

        if state == 'TRANSLATION' or style == 2087:
            if current_stanza is None:
                current_stanza = {'label': '', 'lines': [], 'synonyms': '', 'translation': ''}
            if current_stanza['translation']:
                current_stanza['translation'] += ' ' + txt
            else:
                current_stanza['translation'] = txt
            continue

        if current_stanza and current_stanza.get('translation'):
            purport_paras.append(txt)
        else:
            purport_paras.append(txt)

    if current_stanza:
        stanzas.append(current_stanza)

    return banner, subtitle, stanzas, purport_paras

def parse_tmg_chunk(chunk, raw_title):
    paras = re.split(r'\\par(?![a-zA-Z])', chunk)
    stanzas = []
    current_stanza = None
    purport_paras = []

    clean_t = decode_text(raw_title)
    if '*' in clean_t:
        clean_t = clean_t.split('*')[-1].strip()

    for p in paras:
        sm = re.search(r'\\s(\d+)', p)
        style = int(sm.group(1)) if sm else -1
        txt = decode_text(p)
        if not txt:
            continue

        is_num = bool(re.match(r'^\(?\d+\)?$', txt))
        if is_num or style == 1680:
            if current_stanza:
                stanzas.append(current_stanza)
            num_clean = txt.strip('() ')
            current_stanza = {
                'label': f"Text {num_clean}",
                'lines': [],
                'synonyms': '',
                'translation': ''
            }
            continue

        if style in (2314, 9):
            # If current stanza already has translation, this signals a new stanza!
            if current_stanza and current_stanza.get('translation'):
                stanzas.append(current_stanza)
                current_stanza = None
            if current_stanza is None:
                current_stanza = {'label': '', 'lines': [], 'synonyms': '', 'translation': ''}
            
            # Split on \line to preserve individual poetic verse lines!
            verse_lines = [decode_text(line_part) for line_part in p.split(r'\line') if decode_text(line_part)]
            current_stanza['lines'].extend(verse_lines)
            continue

        if style in (2087, 1522):
            if current_stanza is None:
                current_stanza = {'label': '', 'lines': [], 'synonyms': '', 'translation': ''}
            if current_stanza['translation']:
                current_stanza['translation'] += ' ' + txt
            else:
                current_stanza['translation'] = txt
            continue

        purport_paras.append(txt)

    if current_stanza:
        stanzas.append(current_stanza)

    # For stanzas where translation starts with a number like '1)' or '(1)',
    # clear redundant 'label' so it cleanly matches the authentic style!
    for st in stanzas:
        tr = (st.get('translation') or '').strip()
        if re.match(r'^\(?\d+\)?', tr):
            st['label'] = ''

    return clean_t, stanzas, purport_paras

# ======================================================================
# INGESTION ROUTINES
# ======================================================================

def ingest_btg(conn):
    rtf_path = r"Database\sources\btg 1944-1960.rtf"
    print(f"\n--- Ingesting Back to Godhead (1944–1960) from {rtf_path} ---")
    with open(rtf_path, 'r', encoding='latin-1', errors='ignore') as f:
        data = f.read()

    vol_matches = list(re.finditer(r'\{\\v\\f0\\fs20\\cf6\s*([^\r\n\}]+)', data))
    art_matches = list(re.finditer(r'\{\\v\\f0\\fs20\\cf7\s*([^\r\n\}]+)', data))
    print(f"Found {len(vol_matches)} issues/volumes and {len(art_matches)} articles.")

    cur = conn.cursor()

    cur.execute("""
        INSERT OR REPLACE INTO Books (BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    """, (
        'BTG',
        'BTG',
        'Historical 1944–1960 Issues',
        'Back to Godhead (1944–1960)',
        'Essays & Articles',
        'BTG',
        'His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda',
        47,
        len(art_matches)
    ))

    def get_vol_for_pos(pos):
        current_vol = "1944–1960"
        for vm in vol_matches:
            if vm.start() <= pos:
                current_vol = decode_text(vm.group(1))
            else:
                break
        return current_vol

    inserted = 0
    for i, m in enumerate(art_matches):
        seq = i + 1
        raw_title = m.group(1).strip()
        art_title = decode_text(raw_title)
        clean_title = re.sub(r'^BTG[A-Z0-9]+:\s*', '', art_title)

        start_pos = m.end()
        end_pos = art_matches[i + 1].start() if i + 1 < len(art_matches) else len(data)
        art_rtf = data[start_pos:end_pos]
        paras = clean_rtf_block(art_rtf)

        full_text = "\n\n".join(paras)
        opening_quote = paras[0] if paras else None
        vol_info = get_vol_for_pos(m.start())

        record_key = f"BTG-{seq}"
        reference = f"BTG {seq}"
        display_title = f"{vol_info} — {clean_title}"

        cur.execute("""
            INSERT OR REPLACE INTO Records (
                RecordKey, BookKey, Sequence, ParentKey, RecordType,
                Reference, ReferenceStatus, Title, Devanagari, Transliteration,
                Synonyms, Translation, Purports
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            record_key,
            'BTG',
            seq,
            f"BTG-VOL-{vol_info}",
            'Narrative',
            reference,
            'Active',
            display_title,
            None,
            None,
            None,
            opening_quote,
            full_text
        ))

        cur.execute("""
            INSERT OR REPLACE INTO SearchIndex (
                RecordKey, BookKey, Reference, Devanagari, Transliteration,
                Synonyms, Translation, Purports
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            record_key,
            'BTG',
            reference,
            None,
            None,
            None,
            opening_quote,
            full_text
        ))
        inserted += 1

    print(f"[SUCCESS] Ingested {inserted} BTG articles!")

def ingest_sva(conn):
    rtf_path = r"Database\sources\songs of vaishnav acharyas.rtf"
    print(f"\n--- Ingesting Songs of the Vaiṣṇava Ācāryas from {rtf_path} ---")
    with open(rtf_path, 'r', encoding='latin-1', errors='ignore') as f:
        data = f.read()

    idx_songs = data.find('Songs of the Vaisnava Acaryas')
    idx_temple = data.find('Temple Mantra Guide')
    songs_data = data[idx_songs:idx_temple] if idx_temple != -1 else data[idx_songs:]

    # Match all s7 headers
    s7_matches = list(re.finditer(r'\\s7\s+\\qr\s*\{(.*?)\\par(?![a-zA-Z])\s*\}', songs_data, re.DOTALL))

    # Also capture Foreword and Introduction before match 0
    first_s7 = s7_matches[0].start() if s7_matches else len(songs_data)
    pre_chunk = songs_data[:first_s7]

    all_entries = []
    # Check Foreword
    idx_fwd = pre_chunk.find(r'Foreword')
    idx_intro = pre_chunk.find(r'Introduction')

    if idx_fwd != -1:
        fwd_end = idx_intro if idx_intro != -1 else len(pre_chunk)
        all_entries.append((idx_fwd, fwd_end, "Foreword", "Front Matter"))
    if idx_intro != -1:
        all_entries.append((idx_intro, len(pre_chunk), "Introduction", "Front Matter"))

    for m in s7_matches:
        raw_h = m.group(1)
        clean_t = clean_title_string(raw_h)
        all_entries.append((m.start(), m.end(), clean_t, "Song" if "purport" not in clean_t.lower() else "Purport"))

    print(f"Found {len(all_entries)} entries in Songs of the Vaiṣṇava Ācāryas.")

    cur = conn.cursor()

    # SVA is categorized as 'Books' under Prabhupada's Works!
    cur.execute("""
        INSERT OR REPLACE INTO Books (BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    """, (
        'SVA',
        'SVA',
        'Original Songbook with Word-for-Word Meanings',
        'Songs of the Vaiṣṇava Ācāryas',
        'Books',
        'SVA',
        'His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda / Vaiṣṇava Ācāryas',
        48,
        len(all_entries)
    ))

    sec2_pos = 340999
    sec3_pos = 791998
    sec4_pos = 1044624

    def determine_section(title, pos):
        if "Foreword" in title or "Introduction" in title:
            return 1
        m = re.search(r'SVA\s*(\d)', title)
        if m:
            return int(m.group(1))
        if pos < sec2_pos:
            return 1
        elif pos < sec3_pos:
            return 2
        elif pos < sec4_pos:
            return 3
        else:
            return 4

    sec_counters = {1: 0, 2: 0, 3: 0, 4: 0}
    inserted = 0

    for i, entry in enumerate(all_entries):
        start_p, end_p, clean_t, entry_type = entry
        next_start = all_entries[i + 1][0] if i + 1 < len(all_entries) else len(songs_data)
        chunk = songs_data[end_p:next_start]

        # Determine section number
        sec_num = determine_section(clean_t, start_p)
        sec_counters[sec_num] = sec_counters.get(sec_num, 0) + 1
        song_idx = sec_counters[sec_num]

        record_key = f"SVA-{sec_num}.{song_idx}"
        reference = f"SVA {sec_num}.{song_idx}"

        banner, subtitle, stanzas, purports = parse_sva_song_chunk(chunk)

        is_purport = "purport" in clean_t.lower()
        is_front_matter = entry_type == "Front Matter" or "Glimpse" in clean_t

        if is_purport:
            record_type = 'Purport'
            purport_text = "\n\n".join(purports)
            devanagari = None
            transliteration = None
            synonyms = None
            translation = None
            purports_field = purport_text
        elif is_front_matter or len(stanzas) == 0:
            record_type = 'Narrative'
            narrative_text = "\n\n".join(clean_rtf_block(chunk))
            devanagari = None
            transliteration = None
            synonyms = None
            translation = None
            purports_field = narrative_text
        else:
            record_type = 'Song'
            # Format stanzas
            all_lines = []
            all_syns = []
            all_trans = []
            for st in stanzas:
                lbl = st.get('label', '')
                if lbl:
                    all_lines.append(f"[{lbl}]")
                all_lines.extend(st.get('lines', []))
                if st.get('synonyms'):
                    all_syns.append(st['synonyms'])
                if st.get('translation'):
                    all_trans.append(st['translation'])

            transliteration = "\n".join(all_lines)
            synonyms = "\n\n".join(all_syns)
            translation = "\n\n".join(all_trans)
            purport_commentary = "\n\n".join(purports)

            # Package structured song JSON for rich rendering
            song_obj = {
                "type": "song",
                "bannerTitle": banner or clean_t,
                "subtitle": subtitle,
                "stanzas": stanzas,
                "purport": purport_commentary
            }
            purports_field = json.dumps(song_obj, ensure_ascii=False)

        cur.execute("""
            INSERT OR REPLACE INTO Records (
                RecordKey, BookKey, Sequence, ParentKey, RecordType,
                Reference, ReferenceStatus, Title, Devanagari, Transliteration,
                Synonyms, Translation, Purports
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            record_key,
            'SVA',
            i + 1,
            f"SVA-SEC-{sec_num}",
            record_type,
            reference,
            'Active',
            clean_t,
            None,
            transliteration,
            synonyms,
            translation,
            purports_field
        ))

        # Index in SearchIndex for lightning full-text search
        search_purport = "\n\n".join(purports) if not is_purport and not is_front_matter else purports_field
        cur.execute("""
            INSERT OR REPLACE INTO SearchIndex (
                RecordKey, BookKey, Reference, Devanagari, Transliteration,
                Synonyms, Translation, Purports
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            record_key,
            'SVA',
            reference,
            None,
            transliteration,
            synonyms,
            translation,
            search_purport
        ))
        inserted += 1

    print(f"[SUCCESS] Ingested {inserted} items into Songs of the Vaiṣṇava Ācāryas!")

def ingest_tmg(conn):
    rtf_path = r"Database\sources\temple mantra guide.rtf"
    print(f"\n--- Ingesting Temple Mantra Guide from {rtf_path} ---")
    with open(rtf_path, 'r', encoding='latin-1', errors='ignore') as f:
        data = f.read()

    idx_temple = data.find("Temple Mantra Guide")
    tmg_part = data[idx_temple:]
    matches = list(re.finditer(r'\\s134\b', tmg_part))
    last_toc = matches[-1].end() if matches else 0
    body = tmg_part[last_toc:]

    s489_matches = list(re.finditer(r'\\s489\s+.*?\{(.*?)\\par(?![a-zA-Z])\s*\}', body, re.DOTALL))
    print(f"Found {len(s489_matches)} mantras/prayers in Temple Mantra Guide body.")

    cur = conn.cursor()

    # TMG categorized as 'Books' under Prabhupada's Works!
    cur.execute("""
        INSERT OR REPLACE INTO Books (BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    """, (
        'TMG',
        'TMG',
        'Standard ISKCON Temple Edition',
        'Temple Mantra Guide',
        'Books',
        'TMG',
        'His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda',
        49,
        len(s489_matches)
    ))

    TMG_TITLES = [
        "",
        "Putting on Tilaka",
        "Śrī Guru praṇāma",
        "Śrīla Prabhupāda Praṇati",
        "Śrī Pañca-tattva praṇāma",
        "Hare Kṛṣṇa Mahā-mantra: The Great Chanting for Deliverance",
        "Śrī Śrī Gurv-aṣṭaka",
        "Prema-dhvani Prayers",
        "Śrī Nṛsiṁha Praṇāma",
        "Śrī Tulasī-praṇāma",
        "Śrī Tulasī-pūjā-kīrtana",
        "Śrī Tulasī Pradakṣiṇa Mantra",
        "The Ten Offenses to the Holy Name",
        "Vaiṣṇava-praṇāma",
        "Śrī Śrī Śikṣāṣṭaka",
        "Greeting The Deities",
        "Śrī Guru-vandanā",
        "Jaya Rādhā-Mādhava",
        "Verses Recited Before Śrīmad-Bhāgavatam Class",
        "Śrī Śrī Ṣaḍ-gosvāmy-aṣṭaka",
        "Verses Recited Before Reading Kṛṣṇa Book",
        "Other Kīrtana Chants",
        "Nāma-saṅkīrtana",
        "Purport to Nāma-saṅkīrtana",
        "Śrī Nāma-kīrtana",
        "Gaura-ārati",
        "Sapārṣada-bhagavad-viraha-janita-vilāpa",
        "Śrī Dāmodarāṣṭaka",
        "Śrī Jagannāthāṣṭaka",
        "Prayers for Offering Prasādam",
        "Prayers for Honoring Prasādam"
    ]

    inserted = 0
    for i, m in enumerate(s489_matches):
        num = i + 1
        start_p = m.end()
        end_p = s489_matches[i + 1].start() if i + 1 < len(s489_matches) else len(body)
        chunk = body[start_p:end_p]

        title = TMG_TITLES[num] if num < len(TMG_TITLES) else f"Mantra {num}"
        record_key = f"TMG-{num}"
        reference = f"TMG {num}"

        clean_t, stanzas, purports = parse_tmg_chunk(chunk, m.group(1))

        if len(stanzas) > 0 and any(len(s.get('lines', [])) > 0 for s in stanzas):
            record_type = 'Song'
            all_lines = []
            all_trans = []
            for st in stanzas:
                lbl = st.get('label', '')
                if lbl:
                    all_lines.append(f"[{lbl}]")
                all_lines.extend(st.get('lines', []))
                if st.get('translation'):
                    all_trans.append(st['translation'])

            transliteration = "\n".join(all_lines)
            translation = "\n\n".join(all_trans)
            purport_commentary = "\n\n".join(purports)

            song_obj = {
                "type": "song",
                "bannerTitle": title,
                "subtitle": "",
                "stanzas": stanzas,
                "purport": purport_commentary
            }
            purports_field = json.dumps(song_obj, ensure_ascii=False)
        else:
            record_type = 'Narrative' if "Purport" not in title else 'Purport'
            transliteration = None
            translation = None
            purports_field = "\n\n".join(purports)

        cur.execute("""
            INSERT OR REPLACE INTO Records (
                RecordKey, BookKey, Sequence, ParentKey, RecordType,
                Reference, ReferenceStatus, Title, Devanagari, Transliteration,
                Synonyms, Translation, Purports
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            record_key,
            'TMG',
            num,
            'TMG-ROOT',
            record_type,
            reference,
            'Active',
            title,
            None,
            transliteration,
            None,
            translation,
            purports_field
        ))

        search_purport = "\n\n".join(purports) if record_type == 'Song' else purports_field
        cur.execute("""
            INSERT OR REPLACE INTO SearchIndex (
                RecordKey, BookKey, Reference, Devanagari, Transliteration,
                Synonyms, Translation, Purports
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            record_key,
            'TMG',
            reference,
            None,
            transliteration,
            None,
            translation,
            search_purport
        ))
        inserted += 1

    print(f"[SUCCESS] Ingested {inserted} items into Temple Mantra Guide!")

def main():
    db_path = r"Database\prabhupada_corpus.db"
    backup_path = f"Database/prabhupada_corpus_pre_refine_{datetime.now().strftime('%Y%m%d_%H%M%S')}.db"
    print(f"Creating safety backup at: {backup_path}")
    shutil.copyfile(db_path, backup_path)

    conn = sqlite3.connect(db_path)
    try:
        cur = conn.cursor()
        cur.execute("DELETE FROM Records WHERE BookKey IN ('BTG', 'SVA', 'TMG');")
        cur.execute("DELETE FROM SearchIndex WHERE BookKey IN ('BTG', 'SVA', 'TMG');")
        conn.commit()
        print("Cleared previous records for BTG, SVA, and TMG.")
        ingest_btg(conn)
        ingest_sva(conn)
        ingest_tmg(conn)
        conn.commit()
        print("\nAll database changes committed successfully!")
    finally:
        conn.close()

    print("\n========================================================")
    print(" INGESTION COMPLETE: BTG, SVA, TMG SUCCESSFUL! ")
    print("========================================================")

if __name__ == '__main__':
    main()
