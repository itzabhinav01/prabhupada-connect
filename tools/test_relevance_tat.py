import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

query = "tat"
book_key = "SB"
leadWord = "tat"

sql = f"""
    SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence,
           (CASE 
                WHEN r.Reference LIKE '%{query}%' THEN -1000.0
                WHEN (r.Transliteration LIKE '{leadWord} %' OR r.Transliteration LIKE '{leadWord}\n%')
                 AND (r.Synonyms LIKE '{leadWord}—%' OR r.Synonyms LIKE '{leadWord}-%') THEN -600.0
                WHEN (r.Transliteration LIKE '{leadWord} %' OR r.Transliteration LIKE '{leadWord}\n%') THEN -400.0
                WHEN (r.Synonyms LIKE '{leadWord}—%' OR r.Synonyms LIKE '{leadWord}-%') THEN -200.0
                ELSE 0.0
             END 
             + CASE WHEN length(r.Purports) > 100 THEN -20.0 ELSE 0.0 END
             + bm25(RecordsFts, 25.0, 25.0, 5.0, 4.0, 15.0, 10.0, 5.0, 1.0)) AS RankScore
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    WHERE RecordsFts MATCH '"{query}"' AND r.BookKey = '{book_key}' AND r.RecordKey NOT LIKE '%#%'
    ORDER BY RankScore
    LIMIT 25 OFFSET 0
"""

candidates = c.execute(sql).fetchall()
print(f"Total returned: {len(candidates)}")

print("\n--- Page 1 (Top 25 in RELEVANCE Order) ---")
found_10_14_8 = False
for idx, r in enumerate(candidates, 1):
    print(f"Rank {idx:2d} | Score {r[4]:8.2f} | Seq {r[3]:5d} | {r[0]} | {r[2]}")
    if '10.14-8' in r[0]:
        found_10_14_8 = True

print(f"\nSB 10.14.8 on Page 1: {found_10_14_8}")
