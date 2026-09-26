import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')
row = conn.execute("SELECT Purports FROM Records WHERE RecordKey = 'SB-8.24-61'").fetchone()
print(row[0][-1200:])
conn.close()
