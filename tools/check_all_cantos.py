import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE BookKey = 'SB' AND (Translation LIKE '%END OF THE%' OR Purports LIKE '%END OF THE%')")
for rkey, ref, trans, purp in cur.fetchall():
    for fld, text in [('Translation', trans), ('Purports', purp)]:
        if not text or 'END OF THE' not in text:
            continue
        m = re.search(r'(END OF THE [A-Z]+ CANTO)\b(.*)$', text, re.DOTALL)
        if m:
            tail = m.group(2).strip()
            if tail:
                print(f"[{rkey}] ({ref}) [{fld}]: matched '{m.group(1)}', tail length={len(tail)}")
                print(f"    Tail preview: {repr(tail[:100])}")

conn.close()
