import sqlite3

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

# Test searching for 10.14.8
query = "10.14.8"
fts_query = '"10.14.8"'
sql = f"""
    SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    JOIN Books b ON b.BookKey = r.BookKey
    WHERE RecordsFts MATCH '{fts_query}' AND r.RecordKey NOT LIKE '%#%'
    ORDER BY b.CanonicalOrder, r.Sequence
"""

rows = c.execute(sql).fetchall()
print(f"Results for '{query}': {len(rows)}")
for r in rows:
    print(r)
