import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()

# Test bookmark tags
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%\\*%' OR Purports LIKE '%\\*%'")
rows = cur.fetchall()

print(f"Total rows with '\\*': {len(rows)}")
for rkey, ref, trans, purp in rows:
    text = (trans or '') + ' ' + (purp or '')
    tags = re.findall(r'\\\*[^\*\n]{1,80}\\\*', text)
    if tags:
        print(f"  [{rkey}] ({ref}): {tags}")

conn.close()
