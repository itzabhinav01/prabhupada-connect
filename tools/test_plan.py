import sqlite3

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

sql = """
EXPLAIN QUERY PLAN
SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence
FROM RecordsFts fts
JOIN Records r ON r.rowid = fts.rowid
JOIN Books b ON b.BookKey = r.BookKey
WHERE RecordsFts MATCH '"tat"' AND r.BookKey = 'SB' AND r.RecordKey NOT LIKE '%#%'
ORDER BY b.CanonicalOrder, r.Sequence
LIMIT 50 OFFSET 0
"""

for row in c.execute(sql).fetchall():
    print(row)
