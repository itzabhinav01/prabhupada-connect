import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect("Database/prabhupada_corpus.db")
c = conn.cursor()

# Rank expression used in SqliteCorpusRepository
rank_expr = "bm25(RecordsFts, 25.0, 25.0, 5.0, 4.0, 4.0, 2.0, 5.0, 1.0)"

query = f"""
    SELECT r.RecordKey, r.Reference, {rank_expr} as score
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    WHERE RecordsFts MATCH 'tat' AND r.BookKey = 'SB'
    ORDER BY score
"""
c.execute(query)
rows = c.fetchall()
print(f"Total SB rows matching 'tat': {len(rows)}")

found_idx = -1
for idx, (rk, ref, score) in enumerate(rows):
    if rk == 'SB-10.14-8':
        found_idx = idx
        print(f"SB-10.14-8 is at BM25 rank {idx + 1} with score {score}")
        break

if found_idx == -1:
    print("SB-10.14-8 NOT in the results at all!")

conn.close()
