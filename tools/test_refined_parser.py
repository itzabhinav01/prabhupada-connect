import re
import os
import sys

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

with open(r'C:\VedaBaseModern2\Database\sources\prabhupadashlokas.rtf', 'r', encoding='latin1') as f:
    f.seek(22646000 + 12700)
    data = f.read()

sections = data.split(r'\s489')

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

def parse_all_sps():
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
            if (r'\s7' in p or r'\s2043' in p) and not (r'\s2314' in p or r'\s1962' in p or r'\s2087' in p or r'\s1559' in p):
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

all_records = parse_all_sps()
print(f"Total valid verses extracted: {len(all_records)}")

# Group counts by section
sec_counts = {}
for r in all_records:
    sec_counts[r['sec_name']] = sec_counts.get(r['sec_name'], 0) + 1

for name, count in sec_counts.items():
    print(f"  {name:<35}: {count} verses")

# Verify empty fields
missing_trans = [r['title'] for r in all_records if not r['transliteration']]
missing_transl = [r['title'] for r in all_records if not r['translation']]
print(f"\nMissing transliteration: {len(missing_trans)}")
if missing_trans:
    print("  e.g.:", missing_trans[:5])
print(f"Missing translation: {len(missing_transl)}")
if missing_transl:
    print("  e.g.:", missing_transl[:5])

for r in all_records:
    if 'Padyāvalī 126' in r['title'] or '2.1.2' in r['title']:
        print(f"\n--- {r['title']} ---")
        print("Translit:", repr(r['transliteration']))
        print("Synonyms:", repr(r['synonyms']))
        print("Translation:", repr(r['translation']))
        print("Purports:", repr(r['purports']))
