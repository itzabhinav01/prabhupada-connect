import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%\\*%' OR Purports LIKE '%\\*%'")
rows = cur.fetchall()

print(f"Total rows with '\\*': {len(rows)}")
for rkey, ref, trans, purp in rows:
    for fld, text in [('Translation', trans), ('Purports', purp)]:
        if not text or r'\*' not in text:
            continue
        # Find every snippet around \*
        for m in re.finditer(r'\\\*', text):
            start = max(0, m.start() - 30)
            end = min(len(text), m.end() + 70)
            snippet = text[start:end].replace('\n', '\\n')
            print(f"[{rkey}] ({ref}) [{fld}]: ...{snippet}...")

conn.close()
