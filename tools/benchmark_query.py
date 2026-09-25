import sqlite3
import time

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

query = "tat"
book_key = "SB"

for limit in [50, 100, 200, 500]:
    t0 = time.time()
    sql = f"""
        SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence
        FROM RecordsFts fts
        JOIN Records r ON r.rowid = fts.rowid
        JOIN Books b ON b.BookKey = r.BookKey
        WHERE RecordsFts MATCH '"{query}"' AND r.BookKey = '{book_key}' AND r.RecordKey NOT LIKE '%#%'
        ORDER BY b.CanonicalOrder, r.Sequence
        LIMIT {limit}
    """
    rows = c.execute(sql).fetchall()
    keys = [r[0] for r in rows]
    placeholders = ','.join(['?'] * len(keys))
    snip_sql = f"""
        SELECT RecordKey, snippet(RecordsFts, -1, '«', '»', '...', 25)
        FROM RecordsFts
        WHERE RecordKey IN ({placeholders}) AND RecordsFts MATCH '"{query}"'
    """
    snips = c.execute(snip_sql, keys).fetchall()
    t1 = time.time()
    print(f"Limit {limit}: fetched {len(rows)} rows + {len(snips)} snippets in {(t1-t0)*1000:.1f} ms")
