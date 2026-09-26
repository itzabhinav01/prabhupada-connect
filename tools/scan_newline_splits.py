import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records")
records = cur.fetchall()

print(f"Total records: {len(records)}")

# Pattern: a letter immediately preceding newline, newline, and letter immediately following newline (no spaces!)
split_pattern = re.compile(r'([a-zA-ZāīūṛṝḷḹñṅṇśṣṭḍĀĪŪṚṜḶḸÑṄṆŚṢṬḌ]+)-?\r?\n([a-zA-Zāīūṛṝḷḹñṅṇśṣṭḍ]+)')

total_splits = 0
records_with_splits = 0
sample_splits = []

for rkey, ref, trans, purp in records:
    has_split = False
    for fld, text in [('Translation', trans), ('Purports', purp)]:
        if not text:
            continue
        # Don't check verse stanzas where each line is a line of verse!
        # Wait: in verse stanzas (like Devanagari or transliteration stanzas), each line is a verse line!
        # But in Translation and Purports, let's see!
        # Does a translation or purport ever have lines where a line ends with a word and next line starts with a word WITHOUT space?
        # Let's inspect!
        for m in split_pattern.finditer(text):
            w1 = m.group(1)
            w2 = m.group(2)
            joined = w1 + w2
            total_splits += 1
            has_split = True
            if len(sample_splits) < 40:
                sample_splits.append((rkey, ref, fld, w1, w2, joined))
    if has_split:
        records_with_splits += 1

print(f"Total split occurrences across corpus: {total_splits}")
print(f"Total records affected: {records_with_splits}")
print("\nFirst 40 samples:")
for rkey, ref, fld, w1, w2, joined in sample_splits:
    print(f"  [{rkey}] ({ref}) [{fld}]: '{w1}\\n{w2}' -> '{joined}'")

conn.close()
