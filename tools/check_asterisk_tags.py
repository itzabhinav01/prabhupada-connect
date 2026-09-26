import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%\\*%' OR Purports LIKE '%\\*%'")
rows = cur.fetchall()

print(f"Total rows with '\\*': {len(rows)}")
samples = []
for rkey, bkey, ref, trans, purp in rows:
    text = (trans or '') + ' ' + (purp or '')
    # find all patterns starting with \*
    matches = re.findall(r'\\\*[^\*\n]{1,60}\\\*?', text)
    if matches:
        samples.append((rkey, ref, matches))

print(f"Rows with matching bookmark tags: {len(samples)}")
for rkey, ref, tags in samples[:20]:
    print(f"  [{rkey}] ({ref}): {tags}")

conn.close()
