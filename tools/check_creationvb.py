import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%CreationVB%' OR Purports LIKE '%CreationVB%' OR Translation LIKE '%Canto%' OR Purports LIKE '%Canto%'")
for rkey, ref, trans, purp in cur.fetchall():
    text = (trans or '') + ' ' + (purp or '')
    if 'CreationVB' in text:
        print(f"[{rkey}] ({ref}): Contains CreationVB")
    m = re.search(r'END OF (?:THE )?[A-Z]+ CANTO.*', text, re.IGNORECASE | re.DOTALL)
    if m:
        tail = m.group(0)
        if len(tail) > 40:
            print(f"[{rkey}] ({ref}): {repr(tail[:150])}")

conn.close()
