import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records")
records = cur.fetchall()

print(f"Total records: {len(records)}")

# Pattern: letter at end of line (possibly with hyphen), newline, then letter at start of next line
# But wait! If Line 1 is the end of a sentence or line of text, Line 2 normally starts with a word like "the", "and", etc.
# If Line 2 starts with a LOWERCASE letter, is it a split word OR is it a normal wrapped line?
# In raw Folio text, are paragraphs hard-wrapped at 70-80 columns?
# LET'S CHECK!

sample_record = None
for rkey, ref, trans, purp in records:
    if rkey == 'NOD-4':
        sample_record = (trans or '') + ' ' + (purp or '')
        break

print("=== Raw NOD-4 lines sample ===")
for i, line in enumerate(sample_record.split('\n')[:30]):
    print(f"{i:2d}: {repr(line)}")

conn.close()
