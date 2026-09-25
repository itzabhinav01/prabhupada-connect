import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

print("=== Testing Search for 'athato' ===")
rows = c.execute("""
    SELECT r.RecordKey, r.Reference, r.Title
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    WHERE RecordsFts MATCH 'athato'
""").fetchall()
print(f"Total results: {len(rows)}")
for r in rows[:5]:
    print(' ', r)

print("\n=== Testing Search for 'nityo' in SPS ===")
rows2 = c.execute("""
    SELECT r.RecordKey, r.Reference, r.Title
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    WHERE RecordsFts MATCH 'nityo' AND r.BookKey = 'SPS'
""").fetchall()
print(f"Total results: {len(rows2)}")
for r in rows2:
    print(' ', r)

print("\n=== Testing Search for terms in SPS ===")
for q in ['krsna', 'kṛṣṇa', 'Krsna', 'guru', 'Brahman', 'brahma']:
    rows = c.execute("SELECT COUNT(*) FROM RecordsFts fts JOIN Records r ON r.rowid = fts.rowid WHERE RecordsFts MATCH ? AND r.BookKey = 'SPS'", (q,)).fetchone()
    print(f"Match '{q}' in SPS: {rows[0]}")
