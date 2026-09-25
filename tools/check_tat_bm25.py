import sqlite3

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

tot = c.execute("""
    SELECT COUNT(*) 
    FROM RecordsFts fts 
    JOIN Records r ON r.rowid = fts.rowid 
    WHERE RecordsFts MATCH '"tat"' AND r.BookKey = 'SB'
""").fetchone()[0]
print(f'Total tat matches in SB: {tot}')

row = c.execute("""
    SELECT r.RecordKey, r.Reference, bm25(RecordsFts, 25.0, 25.0, 5.0, 4.0, 15.0, 10.0, 5.0, 1.0) 
    FROM RecordsFts fts 
    JOIN Records r ON r.rowid = fts.rowid 
    WHERE RecordsFts MATCH '"tat"' AND r.RecordKey = 'SB-10.14-8'
""").fetchone()
print(f'SB 10.14.8 row: {row}')

all_bm25 = c.execute("""
    SELECT r.RecordKey, bm25(RecordsFts, 25.0, 25.0, 5.0, 4.0, 15.0, 10.0, 5.0, 1.0) 
    FROM RecordsFts fts 
    JOIN Records r ON r.rowid = fts.rowid 
    WHERE RecordsFts MATCH '"tat"' AND r.BookKey = 'SB' 
    ORDER BY bm25(RecordsFts, 25.0, 25.0, 5.0, 4.0, 15.0, 10.0, 5.0, 1.0)
""").fetchall()

for idx, (rk, score) in enumerate(all_bm25):
    if rk == 'SB-10.14-8':
        print(f'SB 10.14.8 BM25 rank: {idx + 1} of {len(all_bm25)}, score: {score}')
        break
