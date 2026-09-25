import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

# Check what the candidate query returned for SB-1.18-44
sql = """
    SELECT r.RecordKey, r.BookKey, r.Reference, r.Sequence,
           (CASE 
              WHEN r.Reference LIKE '%tat%' THEN 1
              WHEN (r.Transliteration LIKE 'tat %' OR r.Transliteration LIKE 'tat\n%') THEN 2
              WHEN (r.Synonyms LIKE 'tat—%' OR r.Synonyms LIKE 'tat-%') THEN 3
              WHEN (r.Transliteration LIKE '%tat%' OR r.Synonyms LIKE '%tat%') THEN 4
              WHEN r.Translation LIKE '%tat%' THEN 5
              ELSE 6
            END) AS MatchTier
    FROM RecordsFts fts
    JOIN Records r ON r.rowid = fts.rowid
    WHERE RecordsFts MATCH 'tat' AND r.BookKey = 'SB' AND r.RecordKey NOT LIKE '%#%'
    ORDER BY MatchTier, r.Sequence
    LIMIT 200
"""

rows = c.execute(sql).fetchall()
print(f"Total rows: {len(rows)}")
for i, r in enumerate(rows):
    if "1.18-44" in r[0] or "2.1-10" in r[0] or "2.2-25" in r[0]:
        print(f"Index {i}: RecordKey={r[0]}, BookKey={r[1]}, Reference={repr(r[2])}, Sequence={r[3]}, Tier={r[4]}")
