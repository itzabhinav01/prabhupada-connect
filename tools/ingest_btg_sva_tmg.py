import re
import os
import sys
import sqlite3
import shutil
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

def decode_text(t):
    if not t:
        return ""
    for k, v in BALARAM_MAP.items():
        t = t.replace(k, v)
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
    rtf_text = re.sub(r'\\par\b', '___PAR_BREAK___', rtf_text)
    paras = rtf_text.split('___PAR_BREAK___')
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

def ingest_btg(conn):
    rtf_path = r"Database\sources\btg 1944-1960.rtf"
    print(f"\n--- Ingesting Back to Godhead (1944–1960) from {rtf_path} ---")
    with open(rtf_path, 'r', encoding='latin-1', errors='ignore') as f:
        data = f.read()

    # Find volume markers
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

    # Helper to find current volume for article pos
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
        # clean any leading "BTGPY1a: " prefix for cleaner display
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

    idx_songs = data.find(r"{\*\\bkmkstart Songs of the Vaisnava Acaryas}")
    if idx_songs == -1:
        idx_songs = data.find("Songs of the Vaisnava Acaryas")
    idx_temple = data.find("Temple Mantra Guide")
    songs_data = data[idx_songs:idx_temple] if idx_temple != -1 else data[idx_songs:]

    # Match cf7 for Foreword & Introduction and major parts
    # Match cf8 for all song entries
    cf7_matches = list(re.finditer(r'\{\\v\\f0\\fs20\\cf7\s*([^\r\n\}]+)', songs_data))
    cf8_matches = list(re.finditer(r'\{\\v\\f0\\fs20\\cf8\s*([^\r\n\}]+)', songs_data))

    all_entries = []
    # Add Foreword and Introduction from cf7
    for m in cf7_matches:
        t = decode_text(m.group(1))
        if "Foreword" in t or "Introduction" in t:
            all_entries.append((m.start(), m.end(), t, "Front Matter"))

    # Add all songs from cf8
    for m in cf8_matches:
        t = decode_text(m.group(1))
        all_entries.append((m.start(), m.end(), t, "Song"))

    all_entries.sort(key=lambda x: x[0])
    print(f"Found {len(all_entries)} entries in Songs of the Vaiṣṇava Ācāryas.")

    cur = conn.cursor()

    cur.execute("""
        INSERT OR REPLACE INTO Books (BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    """, (
        'SVA',
        'SVA',
        'Original Songbook with Word-for-Word Meanings',
        'Songs of the Vaiṣṇava Ācāryas',
        'Other Works',
        'SVA',
        'Vaiṣṇava Ācāryas',
        48,
        len(all_entries)
    ))

    # Helper to determine canonical section number (1, 2, 3, 4, or 0 for Intro)
    def determine_section(title, pos):
        m = re.search(r'SVA\s*(\d)', title)
        if m:
            return int(m.group(1))
        if "Standard Prayers" in title or "Praṇāma" in title or "Gurv-aṣṭaka" in title or "Bhaktivinoda" in title:
            if "SVA 2" in title: return 2
            if "SVA 3" in title: return 3
            if "SVA 4" in title: return 4
            return 1
        return 1

    SECTION_LABELS = {
        1: "1: Standard Prayers",
        2: "2: Songs of Śrīla Bhaktivinoda Ṭhākura",
        3: "3: Songs of Śrīla Narottama dāsa Ṭhākura",
        4: "4: Songs of Other Vaiṣṇava Ācāryas"
    }

    inserted = 0
    sec_counters = {1: 0, 2: 0, 3: 0, 4: 0}

    for i, entry in enumerate(all_entries):
        start_p, end_p, raw_title, entry_type = entry
        next_start = all_entries[i + 1][0] if i + 1 < len(all_entries) else len(songs_data)
        entry_rtf = songs_data[end_p:next_start]
        paras = clean_rtf_block(entry_rtf)

        full_text = "\n\n".join(paras)
        opening_quote = paras[0] if paras else None

        clean_title = re.sub(r'^\*?\s*SVA\s*\d*:\s*', '', raw_title).strip()
        clean_title = re.sub(r'^\*?\s*', '', clean_title).strip()

        sec_num = determine_section(raw_title, start_p)
        sec_counters[sec_num] = sec_counters.get(sec_num, 0) + 1
        song_idx = sec_counters[sec_num]

        record_key = f"SVA-{sec_num}.{song_idx}"
        reference = f"SVA {sec_num}.{song_idx}"
        sec_label = SECTION_LABELS.get(sec_num, f"Section {sec_num}")
        display_title = f"{sec_label} — {clean_title}"

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
            'Song',
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
            'SVA',
            reference,
            None,
            None,
            None,
            opening_quote,
            full_text
        ))
        inserted += 1

    print(f"[SUCCESS] Ingested {inserted} songs/chapters of Songs of the Vaiṣṇava Ācāryas!")

def ingest_tmg(conn):
    rtf_path = r"Database\sources\temple mantra guide.rtf"
    print(f"\n--- Ingesting Temple Mantra Guide from {rtf_path} ---")
    with open(rtf_path, 'r', encoding='latin-1', errors='ignore') as f:
        data = f.read()

    idx_temple = data.find("Temple Mantra Guide")
    temple_data = data[idx_temple:]
    
    # Body starts around offset 12000 where the actual mantra texts are defined with \s489
    body = temple_data[12000:]
    s489_matches = list(re.finditer(r'\\s489\s+([^\r\n\{]+)?\{([^\}]+)\}', body))
    print(f"Found {len(s489_matches)} mantras/prayers in Temple Mantra Guide body.")

    cur = conn.cursor()

    cur.execute("""
        INSERT OR REPLACE INTO Books (BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    """, (
        'TMG',
        'TMG',
        'Standard ISKCON Temple Edition',
        'Temple Mantra Guide',
        'Other Works',
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
        "Śrī Guru-vandanā The Worship of Śrī Guru",
        "Jaya Rādhā-Mādhava",
        "Verses Recited Before Śrīmad-Bhāgavatam Class",
        "Śrī Śrī Ṣaḍ-gosvāmy-aṣṭaka",
        "Verses Recited Before Reading Kṛṣṇa Book",
        "Other Kīrtana Chants",
        "Nāma-saṅkīrtana",
        "Purport by His Divine Grace A. C. Bhaktivedanta Swami Prabhupāda",
        "Śrī Nāma-kīrtana",
        "Gaura-ārati",
        "Sapārṣada-bhagavad-viraha-janita-vilāpa",
        "Śrī Dāmodarāṣṭaka",
        "Śrī Jagannāthāṣṭaka",
        "Prayers for offering Prasadam",
        "Prayers for honoring Prasadam"
    ]

    inserted = 0
    for i, m in enumerate(s489_matches):
        num = i + 1
        start_p = m.end()
        end_p = s489_matches[i + 1].start() if i + 1 < len(s489_matches) else len(body)
        mantra_rtf = body[start_p:end_p]
        paras = clean_rtf_block(mantra_rtf)

        full_text = "\n\n".join(paras)
        opening_quote = paras[0] if paras else None

        title = TMG_TITLES[num] if num < len(TMG_TITLES) else f"Mantra {num}"
        record_key = f"TMG-{num}"
        reference = f"TMG {num}"
        display_title = f"{num}. {title}"

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
            'Verse',
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
            'TMG',
            reference,
            None,
            None,
            None,
            opening_quote,
            full_text
        ))
        inserted += 1

    print(f"[SUCCESS] Ingested {inserted} items into Temple Mantra Guide!")

def main():
    db_path = r"Database\prabhupada_corpus.db"
    backup_path = f"Database/prabhupada_corpus_pre_ingest_{datetime.now().strftime('%Y%m%d_%H%M%S')}.db"
    print(f"Creating safety backup at: {backup_path}")
    shutil.copyfile(db_path, backup_path)

    conn = sqlite3.connect(db_path)
    try:
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
