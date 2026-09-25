import sqlite3
import sys

sys.stdout.reconfigure(encoding='utf-8')

conn = sqlite3.connect(r'C:\VedaBaseModern2\Database\prabhupada_corpus.db')
c = conn.cursor()

row = c.execute("""
    SELECT RecordKey, Reference, Title, BookKey, Sequence, Transliteration, Synonyms 
    FROM Records 
    WHERE Synonyms LIKE '%adya—from this day%'
""").fetchall()

for r in row:
    print('Key:', r[0])
    print('Ref:', r[1])
    print('Title:', r[2])
    print('BookKey:', r[3])
    print('Sequence:', r[4])
    print('Translit:', r[5][:60])
    print('Synonyms:', r[6][:100])
