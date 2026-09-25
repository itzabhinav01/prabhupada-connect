import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()
rows = c.execute("""
    SELECT RecordKey, Reference, Title 
    FROM Records 
    WHERE BookKey = 'SPS' AND RecordType = 'Verse'
""").fetchall()

print(f"Total SPS verse rows: {len(rows)}")
for r in rows[:15]:
    print(r)

print("\n--- Non-BG/SB/CC samples: ---")
for r in rows:
    if any(k in r[2] for k in ['Upaniṣad', 'Vedānta', 'Bhakti-rasāmṛta', 'Brahma-saṁhitā', 'Purāṇa', 'Hitopadeśa']):
        print(f"{r[0]} | {r[1]} | {r[2]}")
