import sqlite3
import sys
import re
from collections import Counter

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records")
records = cur.fetchall()

pat = re.compile(r'([a-zA-ZāīūṛṝḷḹñṅṇśṣṭḍĀĪŪṚṜḶḸÑṄṆŚṢṬḌ]+)(-?)\r?\n([a-zA-Zāīūṛṝḷḹñṅṇśṣṭḍ]+)')

word_pairs = Counter()
for rkey, ref, trans, purp in records:
    for text in [trans, purp]:
        if not text:
            continue
        for m in pat.finditer(text):
            w1 = m.group(1)
            hyp = m.group(2)
            w2 = m.group(3)
            word_pairs[(w1, hyp, w2)] += 1

print(f"Unique (w1, hyp, w2) triples: {len(word_pairs)}")
print(f"Total occurrences: {sum(word_pairs.values())}")

print("\nTop 50 most frequent split pairs:")
for (w1, hyp, w2), cnt in word_pairs.most_common(50):
    joined = w1 + w2
    print(f"  {cnt:4d}x: '{w1}' + '{hyp}' + '{w2}' -> '{joined}'")

conn.close()
