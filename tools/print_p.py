import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

row = conn.execute("SELECT Purports, Translation FROM Records WHERE RecordKey = 'NOD-4'").fetchone()
text = row[0] or row[1]
for p in text.split('\n\n'):
    if 'topmost devotees' in p:
        print("=== NOD-4 Paragraph ===")
        print(repr(p))
        print("--- Cleaned lines ---")
        print(p)
conn.close()
