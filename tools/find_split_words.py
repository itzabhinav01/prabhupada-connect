import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

cur = conn.cursor()

# 1. Check all occurrences of split words with a space
patterns = [
    (r'\bcaritāmṛ\s+ta\b', 'caritāmṛta'),
    (r'\bAm\s+ṛta\b', 'Amṛta'),
    (r'\bam\s+ṛta\b', 'amṛta'),
    (r'\bBhaktive\s+danta\b', 'Bhaktivedanta'),
    (r'\bBhakti\s+vedanta\b', 'Bhaktivedanta'),
    (r'\bB\s+ṛhaspati\b', 'Bṛhaspati'),
    (r'\bafflic\s+ted\b', 'afflicted'),
]

cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records")
rows = cur.fetchall()

found_fixes = []
for rkey, ref, trans, purp in rows:
    for fld, text in [('Translation', trans), ('Purports', purp)]:
        if not text:
            continue
        for pat, rep in patterns:
            for m in re.finditer(pat, text):
                found_fixes.append((rkey, ref, fld, m.group(0), rep))

print(f"Total split-word occurrences found: {len(found_fixes)}")
for rkey, ref, fld, orig, rep in found_fixes:
    print(f"  [{rkey}] ({ref}) [{fld}]: '{orig}' -> '{rep}'")

conn.close()
