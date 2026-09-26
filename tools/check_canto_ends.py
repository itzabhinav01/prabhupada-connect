import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Purports FROM Records WHERE Purports LIKE '%END OF THE%'")
for rkey, ref, purp in cur.fetchall():
    print(f"[{rkey}] ({ref}):")
    idx = purp.find("END OF THE")
    print(repr(purp[idx:idx+300]))
    print("=" * 60)

conn.close()
