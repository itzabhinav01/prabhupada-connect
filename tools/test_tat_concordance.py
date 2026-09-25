import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect("Database/prabhupada_corpus.db")
c = conn.cursor()

# Run the exact query from ConcordanceService.cs:
sql = """
    SELECT c.RecordKey, c.BookKey, c.Reference, c.Transliteration, c.Synonyms, c.Translation
    FROM Records c
    WHERE c.Synonyms LIKE '%tat%' 
       OR REPLACE(c.Synonyms, '-', '') LIKE '%tat%'
       OR c.Transliteration LIKE '%tat%'
       OR REPLACE(c.Transliteration, '-', '') LIKE '%tat%'
       OR c.Translation LIKE '%tat%'
    ORDER BY c.Sequence ASC
    LIMIT 250;
"""
c.execute(sql)
rows = c.fetchall()
print(f"Total rows returned by Concordance query: {len(rows)}")

sb_keys = [r[0] for r in rows if r[1] == 'SB']
print(f"SB rows in the 250 limit: {len(sb_keys)}")
print("First 5 SB rows:", sb_keys[:5])
print("Last 5 SB rows:", sb_keys[-5:])
print("Is SB-10.14-8 in the 250 results?:", 'SB-10.14-8' in sb_keys)

# Why was SB-10.14-8 cut off?
# What is Sequence of SB-10.14-8 vs the 250th result?
c.execute("SELECT Sequence FROM Records WHERE RecordKey = 'SB-10.14-8'")
sb_seq = c.fetchone()[0]
print("Sequence of SB-10.14-8:", sb_seq)
c.execute("SELECT Sequence FROM Records WHERE RecordKey = ?", (rows[-1][0],))
last_seq = c.fetchone()[0]
print("Sequence of 250th result:", last_seq)

conn.close()
