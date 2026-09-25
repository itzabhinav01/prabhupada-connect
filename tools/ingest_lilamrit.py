import re
import os
import sys
import sqlite3

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

VOLUMES = {
    1: "Volume 1: A Lifetime in Preparation (India 1896–1965)",
    2: "Volume 2: Planting the Seed (New York City 1965–1966)",
    3: "Volume 3: In Every Town and Village (Around the World 1968–1971)",
    4: "Volume 4: India Becomes a Battlefield (1971–1972)",
    5: "Volume 5: Let There Be a Temple (Around the World 1971–1975)",
    6: "Volume 6: Unforeseen Conditions (Around the World 1975–1977)"
}

def get_volume_for_chapter(ch_num):
    if 1 <= ch_num <= 8:
        return 1
    elif 9 <= ch_num <= 19:
        return 2
    elif 20 <= ch_num <= 29:
        return 3
    elif 30 <= ch_num <= 36:
        return 4
    elif 37 <= ch_num <= 45:
        return 5
    elif 46 <= ch_num <= 55:
        return 6
    return 1

def clean_rtf_block(rtf_text):
    for k, v in BALARAM_MAP.items():
        rtf_text = rtf_text.replace(k, v)
    
    # 1. Join words split across newlines before touching RTF tags
    rtf_text = re.sub(r'([a-zA-ZāīūṛṝḷñṅṇṭḍśṣṁṃḥĀĪŪṚṜḶÑṄṆṬḌŚṢṀṂḤ])\r?\n([a-zA-ZāīūṛṝḷñṅṇṭḍśṣṁṃḥĀĪŪṚṜḶÑṄṆṬḌŚṢṀṂḤ])', r'\1\2', rtf_text)

    # 2. Replace \line with space
    rtf_text = rtf_text.replace('\\line', ' ')

    # 3. Handle \pard BEFORE \par! Replace \pard with empty string
    rtf_text = re.sub(r'\\pard\b[^\s\\{}]*', '', rtf_text)

    # 4. Replace \par (whole word only!) with a unique marker
    rtf_text = re.sub(r'\\par\b', '___PAR_BREAK___', rtf_text)

    paras = rtf_text.split('___PAR_BREAK___')
    cleaned_paras = []
    for p in paras:
        p = re.sub(r'\\\*[a-zA-Z]+(?:\d+)?', '', p)
        p = re.sub(r'\\[a-zA-Z]+(?:-?[0-9]+)?\s?', '', p)
        p = re.sub(r"\\'[0-9a-fA-F]{2}", '', p)
        p = p.replace('{', '').replace('}', '').strip()
        p = re.sub(r'\s+', ' ', p)
        # Strip any leading stray single character (like 'd ')
        p = re.sub(r'^[a-zA-Z]\s+(?=[A-Z\"])', '', p)
        if len(p) > 25:
            cleaned_paras.append(p)
    return cleaned_paras

def decode_text(t):
    for k, v in BALARAM_MAP.items():
        t = t.replace(k, v)
    t = re.sub(r'\\\*[a-zA-Z]+(?:\d+)?', '', t)
    t = re.sub(r'\\[a-zA-Z]+(?:-?[0-9]+)?\s?', '', t)
    t = re.sub(r"\\'[0-9a-fA-F]{2}", '', t)
    t = t.replace('{', '').replace('}', '').strip()
    return t

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    rtf_path = r"C:\VedaBaseModern2\Database\sources\lilamrit.rtf"
    db_path = r"C:\VedaBaseModern2\Database\prabhupada_corpus.db"
    
    if not os.path.exists(rtf_path):
        print(f"[ERROR] Source file not found at: {rtf_path}")
        return 1
    if not os.path.exists(db_path):
        print(f"[ERROR] Database file not found at: {db_path}")
        return 1
        
    print(f"Reading {rtf_path}...")
    with open(rtf_path, 'r', encoding='latin-1', errors='ignore') as f:
        text = f.read()

    pattern = re.compile(r'\{\\v\\f0\\fs20\\cf7\s*SPL\s*(\d+):\s*([^\r\n}]+)')
    matches = list(pattern.finditer(text))
    print(f"Found {len(matches)} chapter markers.")
    if len(matches) != 55:
        print(f"[WARN] Expected 55 chapters, but found {len(matches)}.")

    conn = sqlite3.connect(db_path)
    cur = conn.cursor()

    # 1. Ensure Book in Books table
    cur.execute("""
        INSERT OR REPLACE INTO Books (BookKey, Abbreviation, Edition, Title, Category, CorpusId, Author, CanonicalOrder, TotalRecords)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
    """, (
        'SPL',
        'SPL',
        'Original 6 Volumes',
        'Śrīla Prabhupāda-līlāmṛta',
        'Biographies',
        'SPL',
        'Satsvarūpa dāsa Goswami',
        46,
        len(matches)
    ))

    # 2. Process and insert chapters
    inserted_count = 0
    total_words = 0

    for i, m in enumerate(matches):
        ch_num = int(m.group(1))
        ch_title = decode_text(m.group(2))
        vol_num = get_volume_for_chapter(ch_num)
        vol_name = VOLUMES.get(vol_num, f"Volume {vol_num}")
        
        start_pos = m.end()
        end_pos = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        ch_body_rtf = text[start_pos:end_pos]
        paras = clean_rtf_block(ch_body_rtf)
        
        full_purport = "\n\n".join(paras)
        opening_quote = paras[0] if paras else None
        
        record_key = f"SPL-{ch_num}"
        reference = f"SPL {ch_num}"
        display_title = f"{vol_name} — Chapter {ch_num}: {ch_title}"
        
        # Insert into Records
        cur.execute("""
            INSERT OR REPLACE INTO Records (
                RecordKey, BookKey, Sequence, ParentKey, RecordType,
                Reference, ReferenceStatus, Title, Devanagari, Transliteration,
                Synonyms, Translation, Purports
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            record_key,
            'SPL',
            ch_num,
            f"SPL-VOL-{vol_num}",
            'Narrative',
            reference,
            'Active',
            display_title,
            None,
            None,
            None,
            opening_quote,
            full_purport
        ))

        # Insert or update SearchIndex (FTS5)
        cur.execute("""
            INSERT OR REPLACE INTO SearchIndex (
                RecordKey, BookKey, Reference, Devanagari, Transliteration,
                Synonyms, Translation, Purports
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            record_key,
            'SPL',
            reference,
            None,
            None,
            None,
            opening_quote,
            full_purport
        ))

        inserted_count += 1
        words_in_ch = sum(len(p.split()) for p in paras)
        total_words += words_in_ch
        if ch_num in [1, 5, 8, 9, 19, 20, 29, 30, 36, 37, 45, 46, 55]:
            print(f"  Ingested SPL-{ch_num:02d}: {display_title} ({len(paras)} paras, {words_in_ch} words)")

    conn.commit()
    conn.close()

    print("==================================================================")
    print(f"[SUCCESS] Ingested {inserted_count} chapters ({total_words:,} words) of Śrīla Prabhupāda-līlāmṛta with clean text!")
    print("Database updated and FTS SearchIndex synchronized.")
    print("==================================================================")
    return 0

if __name__ == '__main__':
    sys.exit(main())
