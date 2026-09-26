import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

for key in ['NOD-4', 'NOD-5']:
    row = conn.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE RecordKey = ?", (key,)).fetchone()
    if row:
        print(f"=== {row[0]} ({row[1]}) ===")
        text = row[2] or row[3]
        for line in text.split('\n'):
            if any(term in line for term in ['Vṛndāvana', 'Kṛṣ', 'Vaiṣṇ', '\\*']):
                print(repr(line.strip()))

conn.close()
