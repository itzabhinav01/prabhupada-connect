import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

query = "tat"
book_key = "SB"

sql = f"""
    SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    JOIN Books b ON b.BookKey = r.BookKey
    WHERE RecordsFts MATCH '"{query}"' AND r.BookKey = '{book_key}' AND r.RecordKey NOT LIKE '%#%'
    ORDER BY b.CanonicalOrder, r.Sequence
    LIMIT 25 OFFSET 0
"""

candidates = c.execute(sql).fetchall()
print(f"Total returned: {len(candidates)}")

print("\n--- Page 1 (Top 25 in PURE Canonical Order) ---")
for idx, r in enumerate(candidates, 1):
    print(f"Rank {idx:2d} | Seq {r[3]:5d} | {r[0]} | {r[2]}")
