import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect("Database/prabhupada_corpus.db")
c = conn.cursor()

# Test various queries on rowid 20659
for q in ["tat", "tat*", "te", "anukampam", "vipakam", "mukti"]:
    c.execute(f"SELECT rowid FROM RecordsFts WHERE RecordsFts MATCH '{q}' AND rowid = 20659")
    print(f"Match '{q}' on 20659:", c.fetchall())

# What about column specific match?
for col in ["Transliteration", "Synonyms", "Translation", "Purports"]:
    c.execute(f"SELECT rowid FROM RecordsFts WHERE RecordsFts MATCH '{col} : tat' AND rowid = 20659")
    print(f"Match '{col} : tat' on 20659:", c.fetchall())

conn.close()
