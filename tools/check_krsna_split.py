import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%Kṛṣ%ṇa%' OR Purports LIKE '%Kṛṣ%ṇa%' LIMIT 20")
for rkey, ref, trans, purp in cur.fetchall():
    text = (trans or '') + ' ' + (purp or '')
    m = re.findall(r'(\S*Kṛṣ\s+ṇa\S*)', text)
    if m:
        print(f"[{rkey}] ({ref}): {m[:5]}")

conn.close()
