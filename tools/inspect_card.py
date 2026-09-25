import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

rows = c.execute("SELECT RecordKey, BookKey, Reference, Title, Synonyms FROM Records WHERE Synonyms LIKE '%adya%' AND Synonyms LIKE '%pāpam%' ").fetchall()
for row in rows:
    print('RecordKey:', row[0])
    print('BookKey:', row[1])
    print('Reference:', repr(row[2]))
    print('Title:', repr(row[3]))
    print('Synonyms snippet:', repr(row[4][:100]))
    print('---')
