import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records")
records = cur.fetchall()

# Pattern: word-char, optional hyphen, newline, word-char
pat = re.compile(r'([a-zA-ZāīūṛṝḷḹñṅṇśṣṭḍĀĪŪṚṜḶḸÑṄṆŚṢṬḌ]+)(-?)\r?\n([a-zA-Zāīūṛṝḷḹñṅṇśṣṭḍ]+)')

samples = []
for rkey, ref, trans, purp in records:
    for fld, text in [('Translation', trans), ('Purports', purp)]:
        if not text:
            continue
        for m in pat.finditer(text):
            w1 = m.group(1)
            hyphen = m.group(2)
            w2 = m.group(3)
            # context of 30 chars before and after
            start = max(0, m.start() - 25)
            end = min(len(text), m.end() + 25)
            ctx = text[start:end].replace('\n', '\\n')
            samples.append((rkey, ref, w1, hyphen, w2, ctx))

print(f"Total matches: {len(samples)}")
print("\nSamples (every 50th match):")
for rkey, ref, w1, hyphen, w2, ctx in samples[::50][:40]:
    print(f"[{rkey}] ({ref}): w1='{w1}', hyp='{hyphen}', w2='{w2}' --> ctx: {repr(ctx)}")

conn.close()
