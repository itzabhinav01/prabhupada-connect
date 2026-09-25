import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

def test_canonical(query, book_key, limit=25, offset=0):
    clean_query = query.strip()
    book_clause = f"AND r.BookKey = '{book_key}'" if book_key else ""
    sql = f"""
        SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence,
               (CASE 
                  WHEN r.Reference = '{clean_query}' OR r.RecordKey = '{clean_query}' THEN 1
                  WHEN r.Reference LIKE '{clean_query}%' OR r.Reference LIKE '% ' || '{clean_query}%' THEN 2
                  WHEN r.RecordKey LIKE '%{clean_query}%' THEN 3
                  ELSE 4
                END) AS ExactCitationPriority
        FROM RecordsFts fts
        JOIN Records r ON r.rowid = fts.rowid
        JOIN Books b ON b.BookKey = r.BookKey
        WHERE RecordsFts MATCH '"{query}"' {book_clause} AND r.RecordKey NOT LIKE '%#%'
        ORDER BY ExactCitationPriority, b.CanonicalOrder, r.Sequence
        LIMIT {limit} OFFSET {offset}
    """
    rows = c.execute(sql).fetchall()
    return rows

print("--- CANONICAL 'tat' in SB: Page 1 (0-25) ---")
page1 = test_canonical("tat", "SB", 25, 0)
for idx, r in enumerate(page1, 1):
    print(f"Rank {idx:2d} | Seq {r[3]:5d} | {r[0]} | {r[2]}")

print("\n--- CANONICAL 'tat' in SB: Page 2 (25-50) ---")
page2 = test_canonical("tat", "SB", 25, 25)
for idx, r in enumerate(page2, 26):
    print(f"Rank {idx:2d} | Seq {r[3]:5d} | {r[0]} | {r[2]}")

# Verify sequence is strictly non-decreasing
all_rows = page1 + page2
sequences = [r[3] for r in all_rows]
is_strictly_increasing = all(sequences[i] < sequences[i+1] for i in range(len(sequences)-1))
print(f"\nStrictly increasing canonical sequence across Page 1 & 2: {is_strictly_increasing}")
