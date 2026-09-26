import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()

# Find NOD-4 text around "topmost devotees"
row = conn.execute("SELECT Purports, Translation FROM Records WHERE RecordKey = 'NOD-4'").fetchone()
text = row[0] or row[1]
for p in text.split('\n\n'):
    if 'topmost devotees' in p:
        print("=== NOD-4 Paragraph ===")
        print(p)
        print("=======================")

# Find all records with an unclosed '\*' or '\*' with space like '\* '
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%\\*%' OR Purports LIKE '%\\*%'")
rows = cur.fetchall()
print(f"Records with remaining '\\*': {len(rows)}")
for rkey, ref, trans, purp in rows:
    t = (trans or '') + ' ' + (purp or '')
    matches = re.findall(r'\\\*.*', t)
    for m in matches[:2]:
        print(f"  [{rkey}] ({ref}): {m[:100]}")

conn.close()
