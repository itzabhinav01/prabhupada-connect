import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect("Database/prabhupada_corpus.db")
c = conn.cursor()

try:
    c.execute("SELECT r.RecordKey, r.Reference FROM RecordsFts fts JOIN Records r ON r.rowid = fts.rowid WHERE RecordsFts MATCH '10.14.8'")
    print("FTS match for '10.14.8':", c.fetchall())
except Exception as e:
    print("Error querying 10.14.8 directly in FTS:", e)

# In FTS5, '.' is a column separator syntax! e.g. table.column
# So '10.14.8' in FTS5 syntax is invalid or interpreted as column '14.8' of table '10'!
# Let's test with quotes '"10.14.8"':
try:
    c.execute('SELECT r.RecordKey, r.Reference FROM RecordsFts fts JOIN Records r ON r.rowid = fts.rowid WHERE RecordsFts MATCH \'"10.14.8"\'')
    print('FTS match for \'"10.14.8"\':', c.fetchall())
except Exception as e:
    print('Error querying "10.14.8":', e)

# What about "10 14 8"?
try:
    c.execute('SELECT r.RecordKey, r.Reference FROM RecordsFts fts JOIN Records r ON r.rowid = fts.rowid WHERE RecordsFts MATCH \'"10" + "14" + "8"\'')
    print('FTS match for \'"10" + "14" + "8"\':', c.fetchall())
except Exception as e:
    print('Error querying 10 14 8:', e)

conn.close()
