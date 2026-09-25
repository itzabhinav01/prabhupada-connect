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

def clean_rtf(text):
    for k, v in BALARAM_MAP.items():
        text = text.replace(k, v)
    text = re.sub(r'([a-zA-ZāīūṛṝḷñṅṇṭḍśṣṁṃḥĀĪŪṚṜḶÑṄṆṬḌŚṢṀṂḤ])\r?\n([a-zA-ZāīūṛṝḷñṅṇṭḍśṣṁṃḥĀĪŪṚṜḶÑṄṆṬḌŚṢṀṂḤ])', r'\1\2', text)
    text = re.sub(r'\\line\b', '\n', text)
    text = re.sub(r'\\[a-zA-Z0-9*]+ ?', '', text)
    text = re.sub(r'[\{\}]', '', text)
    # If starting with 'd ', clean off the artifact from \par\pard
    text = re.sub(r'^[a-z]\s+', '', text)
    return text.strip()

with open(r'C:\VedaBaseModern2\Database\sources\prabhupadashlokas.rtf', 'r', encoding='latin1') as f:
    f.seek(22646000 + 12700)
    data = f.read()

parts = data.split(r'\s489')

total_verses = 0
for sec_idx, sec in enumerate(parts[1:], 1):
    pars = [p for p in re.split(r'\\par\s*', sec) if p.strip()]
    sec_title = clean_rtf(pars[0]).replace('\n', ' ')
    
    # In each section, count how many verses exist (identifiable by transliteration \s2314 or title \s2043)
    shlokas_in_sec = 0
    verse_titles = []
    
    for p in pars[1:]:
        if r'\s2043' in p:
            cleaned = clean_rtf(p).replace('\n', ' ')
            if cleaned and not any(x in cleaned.upper() for x in ['SYNONYMS', 'TRANSLATION', 'PURPORT']):
                verse_titles.append(cleaned)
                shlokas_in_sec += 1
                
    total_verses += shlokas_in_sec
    print(f"Sec {sec_idx:02d}: {sec_title[:55]:<55} -> {shlokas_in_sec:3d} verses (e.g. {verse_titles[0] if verse_titles else 'None'})")

print(f"\nTOTAL VERSES ACROSS ALL 22 SECTIONS: {total_verses}")
