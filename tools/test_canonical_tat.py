import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

query = "tat"
book_key = "SB"

# Same query as SqliteCorpusRepository
sql = f"""
    SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence,
           (CASE 
              WHEN r.Reference LIKE '%{query}%' THEN 1
              WHEN (r.Transliteration LIKE '{query} %' OR r.Transliteration LIKE '{query}\n%') THEN 2
              WHEN (r.Synonyms LIKE '{query}—%' OR r.Synonyms LIKE '{query}-%') THEN 3
              WHEN (r.Transliteration LIKE '%{query}%' OR r.Synonyms LIKE '%{query}%') THEN 4
              WHEN r.Translation LIKE '%{query}%' THEN 5
              ELSE 6
            END) AS MatchTier
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    WHERE RecordsFts MATCH '"{query}"' AND r.BookKey = '{book_key}' AND r.RecordKey NOT LIKE '%#%'
    ORDER BY bm25(RecordsFts, 25.0, 25.0, 5.0, 4.0, 15.0, 10.0, 5.0, 1.0)
    LIMIT 200
"""

candidates = c.execute(sql).fetchall()
print(f"Total candidates: {len(candidates)}")

# Order by MatchTier then Sequence
ordered = sorted(candidates, key=lambda x: (x[4], x[3]))

print("\n--- Page 1 (Top 25 in Canonical Order) ---")
found_10_14_8 = False
for idx, r in enumerate(ordered[:25], 1):
    print(f"Rank {idx:2d} | Tier {r[4]} | Seq {r[3]:5d} | {r[0]} | {r[2]}")
    if '10.14-8' in r[0]:
        found_10_14_8 = True

print(f"\nSB 10.14.8 on Page 1: {found_10_14_8}")
