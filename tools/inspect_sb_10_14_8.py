import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect("Database/prabhupada_corpus.db")
c = conn.cursor()

c.execute("SELECT RecordKey, Reference, Title, Synonyms, Transliteration, Translation FROM Records WHERE RecordKey LIKE '%10.14.8%' OR Reference LIKE '%10.14.8%'")
rows = c.fetchall()
print(f"Matches for 10.14.8: {len(rows)}")
for r in rows:
    print("RecordKey:", r[0])
    print("Reference:", r[1])
    print("Title:", r[2])
    print("Synonyms:", r[3][:200] if r[3] else None)
    print("Transliteration:\n", r[4])
    print("Translation:\n", r[5])

# Now let's see how FTS searches for 'tat' in SB
c.execute("SELECT COUNT(*) FROM RecordsFts WHERE RecordsFts MATCH 'tat'")
print("Total FTS hits for 'tat':", c.fetchone()[0])

c.execute("SELECT r.RecordKey, r.Reference FROM RecordsFts f JOIN Records r ON f.rowid = r.rowid WHERE RecordsFts MATCH 'tat' AND r.BookKey = 'SB' LIMIT 10")
print("First 10 SB matches from FTS for 'tat':", c.fetchall())

# Does SB 10.14.8 appear in FTS for 'tat'?
c.execute("SELECT r.RecordKey, r.Reference FROM RecordsFts f JOIN Records r ON f.rowid = r.rowid WHERE RecordsFts MATCH 'tat' AND r.RecordKey LIKE '%10.14.8%'")
print("Does 10.14.8 match FTS 'tat'?:", c.fetchall())

conn.close()
