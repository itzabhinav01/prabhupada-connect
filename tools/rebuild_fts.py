import sqlite3

conn = sqlite3.connect("Database/prabhupada_corpus.db")
c = conn.cursor()
try:
    c.execute("INSERT INTO RecordsFts(RecordsFts) VALUES('rebuild')")
    print("RecordsFts rebuilt successfully.")
except Exception as e:
    print("RecordsFts error:", e)

try:
    c.execute("INSERT INTO SearchIndex(SearchIndex) VALUES('rebuild')")
    print("SearchIndex rebuilt successfully.")
except Exception as e:
    print("SearchIndex error:", e)

conn.commit()
conn.close()
