import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

for rkey in ['ĀDI-17-328', 'MADHYA-25-283', 'ANTYA-20-157']:
    row = conn.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE RecordKey = ?", (rkey,)).fetchone()
    if row:
        print(f"=== {row[0]} ({row[1]}) ===")
        print("TRANS:", repr(row[2][-200:] if row[2] else ''))
        print("PURP:", repr(row[3][-300:] if row[3] else ''))

conn.close()
