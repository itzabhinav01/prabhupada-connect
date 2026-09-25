import sqlite3

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

query = 'tat'
book_key = 'SB'

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
    ORDER BY MatchTier, r.Sequence
    LIMIT 50
"""

rows = c.execute(sql).fetchall()
print(f'Total rows: {len(rows)}')
for idx, r in enumerate(rows[:30], 1):
    print(f'Rank {idx:2d} | Tier {r[4]} | Seq {r[3]:5d} | {r[0]} | {r[2]}')
