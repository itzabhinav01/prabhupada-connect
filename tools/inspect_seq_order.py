import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

query = 'tat'
book_key = 'SB'

sql = f"""
    SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence, r.Title,
           snippet(RecordsFts, -1, '«', '»', '...', 25)
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    WHERE RecordsFts MATCH '"{query}"' AND r.BookKey = '{book_key}' AND r.RecordKey NOT LIKE '%#%'
      AND (r.Transliteration GLOB ('*' || '{query}' || '*') OR r.Synonyms GLOB ('*' || '{query}' || '*') OR r.Translation GLOB ('*' || '{query}' || '*') OR r.Purports GLOB ('*' || '{query}' || '*') OR r.Reference GLOB ('*' || '{query}' || '*'))
    ORDER BY r.Sequence
    LIMIT 25
"""

rows = c.execute(sql).fetchall()
print(f'Total rows in pure sequence order: {len(rows)}')
for idx, r in enumerate(rows, 1):
    print(f'{idx:2d} | Seq: {r[3]:5d} | Key: {r[0]} | Ref: {repr(r[2])} | Title: {repr(r[4])}')
    print(f'     Snippet: {repr(r[5][:80])}')
