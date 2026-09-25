import sqlite3

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

count_res = c.execute("""
    SELECT COUNT(*) 
    FROM RecordsFts fts 
    JOIN Records r ON r.rowid = fts.rowid 
    WHERE RecordsFts MATCH 'dharma' AND r.RecordKey NOT LIKE '%#%'
""").fetchone()
print('FTS Match count:', count_res[0])

query_sql = """
    SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence,
           (CASE 
              WHEN r.Reference LIKE '%dharma%' THEN 1
              WHEN (r.Transliteration LIKE 'dharma %' OR r.Transliteration LIKE 'dharma\n%') THEN 2
              WHEN (r.Synonyms LIKE 'dharma—%' OR r.Synonyms LIKE 'dharma-%') THEN 3
              WHEN (r.Transliteration LIKE '%dharma%' OR r.Synonyms LIKE '%dharma%') THEN 4
              WHEN r.Translation LIKE '%dharma%' THEN 5
              ELSE 6
            END) AS MatchTier
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    WHERE RecordsFts MATCH 'dharma' AND r.RecordKey NOT LIKE '%#%'
    ORDER BY bm25(RecordsFts, 25.0, 25.0, 5.0, 4.0, 15.0, 10.0, 5.0, 1.0)
    LIMIT 200
"""
rows = c.execute(query_sql).fetchall()
print('Query fetched rows count:', len(rows))
if rows:
    print('First row:', rows[0])
