import sqlite3
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

rows = c.execute("""
    SELECT RecordKey, Reference, Title 
    FROM Records 
    WHERE BookKey = 'SPS' AND RecordType = 'Verse'
""").fetchall()

mappings = {}
for rk, ref, title in rows:
    if not title:
        continue
    # If title has scripture name and numbers e.g. "Kaṭha Upaniṣad 2.2.13", "Bhakti-rasāmṛta-sindhu 1.1.11", "Vedānta Sūtra 1.1.1"
    # Ignore pure BG, SB, CC
    if any(title.startswith(x) for x in ['Bhagavad-gītā', 'Śrīmad-Bhāgavatam', 'Caitanya-caritāmṛta']):
        continue
    
    clean_title = title.strip()
    mappings[clean_title] = ref

print(f"Total non-BG/SB/CC mapped titles: {len(mappings)}")
for k, v in list(mappings.items())[:40]:
    print(f"  '{k}' -> '{v}'")
