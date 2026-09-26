import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect('Database/prabhupada_corpus.db')
cur = conn.cursor()

cur.execute("SELECT RecordKey, BookKey, Reference, Translation, Purports FROM Records WHERE Translation LIKE '%Thus end%' OR Purports LIKE '%Thus end%'")
rows = cur.fetchall()

print(f"Total rows inspected: {len(rows)}")
count = 0
for rkey, bkey, ref, trans, purp in rows:
    for fld, text in [('Translation', trans), ('Purports', purp)]:
        if not text:
            continue
        m = re.search(r'Thus end the Bhaktivedanta purports to.*?\.', text, re.IGNORECASE)
        if m:
            tail = text[m.end():].strip()
            tail_clean = tail.lstrip(' "\'')
            if tail_clean and any(k in tail_clean for k in ['Chapter', '—', ':', 'Canto', 'Ādi', 'Adi', 'Madhya', 'Antya', 'SB ']):
                count += 1
                end_snippet = text[max(0, m.start()):m.end()]
                print(f"[{count}] {rkey} ({ref}) [{fld}]:")
                print(f"    END: ...{end_snippet[-60:]}")
                print(f"    LEAK ({len(tail)} chars): {tail[:120]}...")
                print("-" * 60)

conn.close()
