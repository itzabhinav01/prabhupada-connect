import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

row = conn.execute("SELECT Purports, Translation FROM Records WHERE RecordKey = 'NOD-4'").fetchone()
text = row[0] or row[1]
paras = text.split('\n\n')
for i, p in enumerate(paras):
    if 'topmost devotees' in p or '*Vṛndāvana is the transcendental' in p:
        print(f"=== Paragraph {i} ===")
        print(repr(p))

conn.close()
