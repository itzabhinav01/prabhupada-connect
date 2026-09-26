import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE BookKey IN ('ĀDI', 'MADHYA', 'ANTYA') AND (Translation LIKE '%END OF%' OR Purports LIKE '%END OF%')")
for rkey, ref, trans, purp in cur.fetchall():
    print(f"[{rkey}] ({ref}):")
    text = (trans or '') + ' ' + (purp or '')
    idx = text.upper().find("END OF")
    print(repr(text[idx:idx+200]))

conn.close()
