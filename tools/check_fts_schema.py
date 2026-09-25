import sqlite3

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()
row = c.execute("SELECT sql FROM sqlite_master WHERE name = 'RecordsFts'").fetchone()
print(row[0] if row else "None")
