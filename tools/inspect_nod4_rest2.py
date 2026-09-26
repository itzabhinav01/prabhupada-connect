import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

row = conn.execute("SELECT Purports, Translation FROM Records WHERE RecordKey = 'NOD-4'").fetchone()
text = row[0] or row[1]
lines = text.split('\n')
for i in range(70, min(len(lines), 115)):
    print(f"{i:2d}: {repr(lines[i])}")

conn.close()
