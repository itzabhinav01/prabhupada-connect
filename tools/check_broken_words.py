import sqlite3
import sys
import re

sys.stdout.reconfigure(encoding='utf-8')
conn = sqlite3.connect('Database/prabhupada_corpus.db')

words_to_check = [
    (r'\bcaritāmṛ\s+ta\b', 'caritāmṛta'),
    (r'\bAm\s+ṛta\b', 'Amṛta'),
    (r'\bam\s+ṛta\b', 'amṛta'),
    (r'\bBhakti\s+vedanta\b', 'Bhaktivedanta'),
    (r'\bBhaktive\s+danta\b', 'Bhaktivedanta'),
    (r'\bafflic\s+ted\b', 'afflicted'),
    (r'\bPrabhu\s+pāda\b', 'Prabhupāda'),
    (r'\bMāyā\s+vāda\b', 'Māyāvāda'),
    (r'\bKṛṣ\s+ṇa\b', 'Kṛṣṇa'),
    (r'\bCai\s+tanya\b', 'Caitanya'),
    (r'\bB\s+ṛhaspati\b', 'Bṛhaspati'),
    (r'\bb\s+ṛhaspati\b', 'bṛhaspati'),
    (r'\bVṛtrā\s+sura\b', 'Vṛtrāsura'),
    (r'\bvṛtrā\s+sura\b', 'vṛtrāsura'),
]

for pat, rep in words_to_check:
    cur = conn.cursor()
    # SQL query
    sql_like = '%' + rep[:3] + '%'
    rows = cur.execute("SELECT COUNT(*) FROM Records WHERE Translation REGEXP ? OR Purports REGEXP ?", (pat, pat)).fetchone() if False else None

# Python scan across Records
cur = conn.cursor()
cur.execute("SELECT RecordKey, Reference, Translation, Purports FROM Records")
matches = {rep: 0 for _, rep in words_to_check}
for rkey, ref, trans, purp in cur.fetchall():
    text = (trans or '') + ' ' + (purp or '')
    for pat, rep in words_to_check:
        if re.search(pat, text):
            matches[rep] += 1

print("Broken word occurrences:")
for rep, cnt in matches.items():
    print(f"  {rep}: {cnt} occurrences")

conn.close()
