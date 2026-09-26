import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

for i in range(1, 19):
    # Find last verse of chapter i
    rows = conn.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records WHERE BookKey = 'BG' AND RecordKey LIKE ? ORDER BY Sequence DESC LIMIT 1", (f"BG-{i}-%",)).fetchall()
    for r in rows:
        text = (r[2] or '') + ' ' + (r[3] or '')
        idx = text.find("Thus end")
        print(f"=== {r[0]} ({r[1]}) ===")
        if idx != -1:
            print("END:", repr(text[idx:idx+150]))
            print("TAIL:", repr(text[idx+150:]))
        else:
            print("No 'Thus end' found. Last 100 chars:", repr(text[-100:]))

conn.close()
