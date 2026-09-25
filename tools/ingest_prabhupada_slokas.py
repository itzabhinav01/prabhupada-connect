import re
import os
import sys
import sqlite3

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

def clean_rtf(text, preserve_newlines=False):
    for k, v in BALARAM_MAP.items():
        text = text.replace(k, v)
    text = re.sub(r'([a-zA-ZāīūṛṝḷñṅṇṭḍśṣṁṃḥĀĪŪṚṜḶÑṄṆṬḌŚṢṀṂḤ])\r?\n([a-zA-ZāīūṛṝḷñṅṇṭḍśṣṁṃḥĀĪŪṚṜḶÑṄṆṬḌŚṢṀṂḤ])', r'\1\2', text)
    if preserve_newlines:
        text = re.sub(r'\\line\b', '\n', text)
    else:
        text = re.sub(r'\\line\b', ' ', text)
    text = re.sub(r'\\[a-zA-Z0-9*]+ ?', '', text)
    text = re.sub(r'[\{\}]', '', text)
    text = re.sub(r'^[a-z]\s+', '', text)
    return text.strip()

SECTION_NAMES = [
    "",
    "Auspicious Invocation Mantras",
    "Śrī Śrī Gurv-aṣṭaka",
    "Śrī Śrī Ṣaḍ-gosvāmy-aṣṭaka",
    "Śrī Śrī Śikṣāṣṭaka",
    "Bhagavad-gītā",
    "Śrīmad-Bhāgavatam",
    "Caitanya-caritāmṛta",
    "Śrī Brahma-saṁhitā",
    "Vedānta-sūtra",
    "The Upaniṣads",
    "Caitanya Bhāgavata",
    "Six Gosvāmīs & Others",
    "Purāṇas",
    "Mahābhārata",
    "Other Vedic Literatures",
    "Previous Ācāryas",
    "Bhaktivinoda Ṭhākura",
    "Narottama dāsa Ṭhākura",
    "Jayadeva Gosvāmī",
    "Nīti-śāstra",
    "Non Devotees",
    "Quotes from Other Sources"
]

def parse_all_sps(rtf_path):
    with open(rtf_path, 'r', encoding='latin1') as f:
        f.seek(22646000 + 12700)
        data = f.read()

    sections = data.split(r'\s489')
    all_records = []

    for sec_idx in range(1, len(sections)):
        sec_content = sections[sec_idx]
        paragraphs = re.split(r'\\par\s*', sec_content)
        
        current_title = ""
        current_verse = None
        sub_index = 0
        state = "NONE"
        sec_name = SECTION_NAMES[sec_idx] if sec_idx < len(SECTION_NAMES) else f"Section {sec_idx}"

        def finish_verse():
            nonlocal current_verse
            if current_verse:
                if not current_verse["translation"] and current_verse["synonyms"]:
                    m = re.search(r'\.\s{2,}([A-Z][^—–]+)$', current_verse["synonyms"])
                    if m:
                        current_verse["translation"] = m.group(1).strip()
                        current_verse["synonyms"] = current_verse["synonyms"][:m.start()+1].strip()
                if current_verse["transliteration"] or current_verse["translation"]:
                    all_records.append(current_verse)
                current_verse = None

        for p in paragraphs:
            if not p.strip():
                continue
                
            cleaned_single = clean_rtf(p, preserve_newlines=False)
            cleaned_multi = clean_rtf(p, preserve_newlines=True)
            upper = cleaned_single.upper()
            
            # Check for section title or section headings
            if any(k in upper for k in ['SELECTED VERSES', 'AUSPICIOUS INVOCATION', 'GOVINDAM PRAYERS', 'QUOTES FROM', 'VERSES BY NON DEVOTEES']):
                continue
                
            if upper == 'SYNONYMS':
                state = "SYNONYMS"
                continue
            elif upper == 'TRANSLATION':
                state = "TRANSLATION"
                continue
            elif upper == 'PURPORT':
                state = "PURPORT"
                continue
                
            # Is this a title paragraph?
            is_title = False
            if (r'\s7' in p or r'\s2043' in p) and not (r'\s2314' in p or r'\s2348' in p or r'\s1962' in p or r'\s2087' in p or r'\s1559' in p):
                if cleaned_single and len(cleaned_single) < 150:
                    is_title = True

            if is_title:
                if current_verse is None or (not current_verse["transliteration"] and not current_verse["translation"]):
                    current_title = cleaned_single
                    sub_index = 1
                    current_verse = {
                        "sec_idx": sec_idx,
                        "sec_name": sec_name,
                        "title": current_title,
                        "sub_index": sub_index,
                        "transliteration": "",
                        "synonyms": "",
                        "translation": "",
                        "purports": ""
                    }
                    state = "TITLE"
                else:
                    finish_verse()
                    current_title = cleaned_single
                    sub_index = 1
                    current_verse = {
                        "sec_idx": sec_idx,
                        "sec_name": sec_name,
                        "title": current_title,
                        "sub_index": sub_index,
                        "transliteration": "",
                        "synonyms": "",
                        "translation": "",
                        "purports": ""
                    }
                    state = "TITLE"
                continue

            # Transliteration
            if r'\s2314' in p or r'\s2348' in p:
                if current_verse and current_verse["translation"]:
                    finish_verse()
                    sub_index += 1
                    stanza_title = f"{current_title} ({sub_index})" if current_title else f"Stanza {sub_index}"
                    current_verse = {
                        "sec_idx": sec_idx,
                        "sec_name": sec_name,
                        "title": stanza_title,
                        "sub_index": sub_index,
                        "transliteration": "",
                        "synonyms": "",
                        "translation": "",
                        "purports": ""
                    }
                elif current_verse is None:
                    sub_index += 1
                    current_verse = {
                        "sec_idx": sec_idx,
                        "sec_name": sec_name,
                        "title": current_title or f"Verse {sub_index}",
                        "sub_index": sub_index,
                        "transliteration": "",
                        "synonyms": "",
                        "translation": "",
                        "purports": ""
                    }
                    
                state = "TRANSLIT"
                if current_verse["transliteration"]:
                    current_verse["transliteration"] += "\n" + cleaned_multi
                else:
                    current_verse["transliteration"] = cleaned_multi
                continue

            if current_verse is None:
                continue

            # Synonyms
            if state == "SYNONYMS" or r'\s1962' in p:
                state = "SYNONYMS"
                if current_verse["synonyms"]:
                    current_verse["synonyms"] += " " + cleaned_single
                else:
                    current_verse["synonyms"] = cleaned_single
                continue

            # Translation
            if state == "TRANSLATION" or r'\s2087' in p:
                state = "TRANSLATION"
                if current_verse["translation"]:
                    current_verse["translation"] += "\n\n" + cleaned_single
                else:
                    current_verse["translation"] = cleaned_single
                continue

            # Purport / Note
            if state in ["PURPORT", "TRANSLATION"] or r'\s1559' in p:
                state = "PURPORT"
                if current_verse["purports"]:
                    current_verse["purports"] += "\n\n" + cleaned_multi
                else:
                    current_verse["purports"] = cleaned_multi

        finish_verse()

    return all_records

def ingest():
    db_path = r"C:\VedaBaseModern2\Database\prabhupada_corpus.db"
    rtf_path = r"C:\VedaBaseModern2\Database\sources\prabhupadashlokas.rtf"

    print("Parsing Prabhupada Shlokas RTF...")
    records = parse_all_sps(rtf_path)
    print(f"Extracted {len(records)} verses.")

    conn = sqlite3.connect(db_path)
    c = conn.cursor()

    # 1. Clean existing SPS if re-running
    print("Clearing any prior SPS entries...")
    c.execute("DELETE FROM Records WHERE BookKey = 'SPS'")
    c.execute("DELETE FROM Books WHERE BookKey = 'SPS'")

    # 2. Find max sequence
    c.execute("SELECT MAX(Sequence) FROM Records")
    row = c.execute("SELECT MAX(Sequence) FROM Records").fetchone()
    base_seq = (row[0] or 50000) + 1
    print(f"Starting Sequence at: {base_seq}")

    # 3. Insert Books row
    # BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords, IsPdf, PdfPath
    c.execute("""
        INSERT INTO Books (BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords, IsPdf, PdfPath)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    """, (
        "SPS",
        "SPS",
        "1992",
        "Śrīla Prabhupāda Ślokas",
        "Other Works",
        "prabhupada",
        "His Divine Grace A.C. Bhaktivedanta Swami Prabhupāda",
        17,
        len(records),
        0,
        None
    ))

    # 4. Insert section chapter records and verse records
    current_seq = base_seq
    sec_verse_count = {}

    # Insert 22 section nodes
    for sec_i in range(1, len(SECTION_NAMES)):
        s_name = SECTION_NAMES[sec_i]
        sec_rk = f"SPS-SEC-{sec_i}"
        sec_ref = f"SPS Section {sec_i}"
        c.execute("""
            INSERT INTO Records (RecordKey, BookKey, Sequence, ParentKey, RecordType, Reference, ReferenceStatus, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            sec_rk,
            "SPS",
            current_seq,
            None,
            "Chapter",
            sec_ref,
            "Valid",
            s_name,
            "",
            "",
            "",
            "",
            ""
        ))
        current_seq += 1

    # Insert individual verses
    for r in records:
        sec_idx = r["sec_idx"]
        sec_verse_count[sec_idx] = sec_verse_count.get(sec_idx, 0) + 1
        v_num = sec_verse_count[sec_idx]
        
        rk = f"SPS-{sec_idx}.{v_num}"
        ref = f"SPS {sec_idx}.{v_num}"
        parent_key = f"SPS-SEC-{sec_idx}"
        
        c.execute("""
            INSERT INTO Records (RecordKey, BookKey, Sequence, ParentKey, RecordType, Reference, ReferenceStatus, Title, Devanagari, Transliteration, Synonyms, Translation, Purports)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            rk,
            "SPS",
            current_seq,
            parent_key,
            "Verse",
            ref,
            "Valid",
            r["title"],
            "",
            r["transliteration"],
            r["synonyms"],
            r["translation"],
            r["purports"]
        ))
        current_seq += 1

    conn.commit()
    print(f"Successfully inserted {len(records)} verses and 22 section headers into Records table.")

    # 5. Rebuild RecordsFts
    print("Rebuilding RecordsFts...")
    try:
        c.execute("INSERT INTO RecordsFts(RecordsFts) VALUES('rebuild')")
        print("RecordsFts rebuild completed.")
    except Exception as ex:
        print("Error rebuilding RecordsFts:", ex)

    # 6. Rebuild SearchIndex if exists
    try:
        c.execute("INSERT INTO SearchIndex(SearchIndex) VALUES('rebuild')")
        print("SearchIndex rebuild completed.")
    except Exception as ex:
        print("SearchIndex notice/rebuild:", ex)

    conn.commit()
    conn.close()
    print("Ingestion complete and database committed successfully!")

if __name__ == "__main__":
    ingest()
