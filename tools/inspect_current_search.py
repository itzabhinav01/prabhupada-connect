import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

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
            END) AS MatchTier,
           r.Title,
           r.Transliteration,
           r.Synonyms
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    WHERE RecordsFts MATCH '"{query}"' AND r.BookKey = '{book_key}' AND r.RecordKey NOT LIKE '%#%'
      AND (r.Transliteration GLOB ('*' || '{query}' || '*') OR r.Synonyms GLOB ('*' || '{query}' || '*') OR r.Translation GLOB ('*' || '{query}' || '*') OR r.Purports GLOB ('*' || '{query}' || '*') OR r.Reference GLOB ('*' || '{query}' || '*'))
    ORDER BY MatchTier, r.Sequence
    LIMIT 20
"""

rows = c.execute(sql).fetchall()
print(f'Total rows: {len(rows)}')
for idx, r in enumerate(rows, 1):
    print(f'Rank {idx:2d} | Tier {r[4]} | Seq {r[3]:5d} | Key: {r[0]} | Ref: {repr(r[2])} | Title: {repr(r[5])}')
    print('  Translit starts:', repr(r[6][:40] if r[6] else ''))
    print('  Synonyms starts:', repr(r[7][:40] if r[7] else ''))
