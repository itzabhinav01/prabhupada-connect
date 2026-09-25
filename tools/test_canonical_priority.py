import sqlite3

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

def test_query(query, book_key=None):
    clean_query = query.strip()
    book_clause = f"AND r.BookKey = '{book_key}'" if book_key else ""
    sql = f"""
        SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence,
               (CASE 
                  WHEN r.Reference = '{clean_query}' OR r.RecordKey = '{clean_query}' THEN 1
                  WHEN r.Reference LIKE '{clean_query}%' OR r.Reference LIKE '% ' || '{clean_query}%' THEN 2
                  WHEN r.RecordKey LIKE '%{clean_query}%' THEN 3
                  ELSE 4
                END) AS Priority
        FROM RecordsFts fts
        JOIN Records r ON r.rowid = fts.rowid
        JOIN Books b ON b.BookKey = r.BookKey
        WHERE RecordsFts MATCH '"{query}"' {book_clause} AND r.RecordKey NOT LIKE '%#%'
        ORDER BY Priority, b.CanonicalOrder, r.Sequence
        LIMIT 25 OFFSET 0
    """
    rows = c.execute(sql).fetchall()
    print(f"\nResults for '{query}' (Book: {book_key}):")
    for idx, r in enumerate(rows, 1):
        print(f"Rank {idx:2d} | Prio {r[4]} | Seq {r[3]:5d} | {r[0]} | {r[2]}")

test_query("tat", "SB")
test_query("10.14.8", None)
test_query("BG 2.13", None)
