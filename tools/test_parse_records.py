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
    "", # 0 dummy
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

all_records = []

for sec_idx in range(1, len(sections)):
    sec_content = sections[sec_idx]
    # Split by \par
    paragraphs = re.split(r'\\par\s*', sec_content)
    
    current_shloka = None
    shloka_counter = 0
    state = "NONE" # TITLE, TRANSLIT, SYNONYMS, TRANSLATION, PURPORT
    
    for p in paragraphs:
        if not p.strip():
            continue
            
        cleaned_single = clean_rtf(p, preserve_newlines=False)
        cleaned_multi = clean_rtf(p, preserve_newlines=True)
        
        # Check if this paragraph is a Verse Title (\s2043 or \s7)
        # Note: SYNONYMS and TRANSLATION also use \s2043
        is_title = False
        if r'\s2043' in p or r'\s7' in p:
            upper = cleaned_single.upper()
            if upper == 'SYNONYMS':
                state = "SYNONYMS"
                continue
            elif upper == 'TRANSLATION':
                state = "TRANSLATION"
                continue
            elif upper == 'PURPORT':
                state = "PURPORT"
                continue
            elif cleaned_single and not any(k in upper for k in ['SELECTED VERSES', 'AUSPICIOUS INVOCATION', 'GOVINDAM PRAYERS', 'QUOTES FROM']):
                is_title = True
                
        if is_title:
            if current_shloka:
                all_records.append(current_shloka)
            shloka_counter += 1
            sec_name = SECTION_NAMES[sec_idx] if sec_idx < len(SECTION_NAMES) else f"Section {sec_idx}"
            current_shloka = {
                "sec_idx": sec_idx,
                "sec_name": sec_name,
                "shloka_idx": shloka_counter,
                "title": cleaned_single,
                "transliteration": "",
                "synonyms": "",
                "translation": "",
                "purports": ""
            }
            state = "TITLE"
            continue
            
        if not current_shloka:
            continue
            
        if r'\s2314' in p:
            # Transliteration
            state = "TRANSLIT"
            if current_shloka["transliteration"]:
                current_shloka["transliteration"] += "\n" + cleaned_multi
            else:
                current_shloka["transliteration"] = cleaned_multi
        elif state == "SYNONYMS" or r'\s1962' in p:
            state = "SYNONYMS"
            if current_shloka["synonyms"]:
                current_shloka["synonyms"] += "\n" + cleaned_single
            else:
                current_shloka["synonyms"] = cleaned_single
        elif state == "TRANSLATION" or r'\s2087' in p:
            state = "TRANSLATION"
            if current_shloka["translation"]:
                current_shloka["translation"] += "\n\n" + cleaned_single
            else:
                current_shloka["translation"] = cleaned_single
        elif state in ["TRANSLATION", "PURPORT"] or r'\s1559' in p:
            # Purport / Note / Citation
            state = "PURPORT"
            if current_shloka["purports"]:
                current_shloka["purports"] += "\n\n" + cleaned_multi
            else:
                current_shloka["purports"] = cleaned_multi

    if current_shloka:
        all_records.append(current_shloka)

print(f"Total records parsed: {len(all_records)}")

# Show samples from different sections
for idx in [0, 18, 50, 270, 520, 600, 650, 700, 750, 800, 850]:
    if idx < len(all_records):
        r = all_records[idx]
        print(f"\n--- [{idx+1}] Sec {r['sec_idx']} ({r['sec_name']}) #{r['shloka_idx']}: {r['title']} ---")
        print("Translit:", (r['transliteration'][:100] + '...') if len(r['transliteration']) > 100 else r['transliteration'])
        print("Synonyms:", (r['synonyms'][:80] + '...') if len(r['synonyms']) > 80 else r['synonyms'])
        print("Translation:", (r['translation'][:100] + '...') if len(r['translation']) > 100 else r['translation'])
        if r['purports']:
            print("Purports/Notes:", (r['purports'][:100] + '...') if len(r['purports']) > 100 else r['purports'])
