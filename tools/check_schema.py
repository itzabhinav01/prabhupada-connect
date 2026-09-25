import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()
print('=== Books schema ===')
for row in c.execute("PRAGMA table_info(Books)"):
    print(' ', row)

print('\n=== Records schema ===')
for row in c.execute("PRAGMA table_info(Records)"):
    print(' ', row)

print('\n=== Existing Books ===')
for row in c.execute("SELECT BookKey, Title, Author, Category FROM Books"):
    print(' ', row)
