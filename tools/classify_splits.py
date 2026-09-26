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

# Common English stop words / complete words that should NOT be joined to the next word unless hyphenated
COMMON_WORDS = set("""
a about above after again against all also am an and another any are as at 
be because been before being between both but by 
came can cannot could 
day did do does down during 
each even every 
first for from 
get give go good great 
had has have he her here him his how 
i if in into is it its 
just 
know 
last like little long lord 
made make man many may me men might more most much must my 
never new no not now 
of off old on one only or other our out over own 
people 
right 
said same see she should since so some state still such 
take than that the their them then there these they this those three through time to too two 
under up upon us used 
very 
was way we well were what when where which while who will with without world would 
year years you your
""".split())

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

to_join = []
to_space = []

for (w1, hyp, w2), cnt in word_pairs.items():
    w1_lower = w1.lower()
    w2_lower = w2.lower()
    
    # If hyphenated: check if it's a line-break split word
    if hyp == '-':
        to_join.append(((w1, hyp, w2), cnt, w1 + w2))
        continue
        
    # If w1 is a complete common word AND w2 is not a known diacritic fragment (like ṇa, ṣṇa, ṭha, ḍita, etc.)
    # Diacritic fragments that are never standalone words:
    is_diacritic_fragment = w2_lower in {'ṇa', 'ṇas', 'ṣṇa', 'ṣṇas', 'ṣa', 'ṣas', 'ṭha', 'ṭhas', 'ḍita', 'ḍitas', 
                                         'ṇī', 'ṇīs', 'ācārya', 'ācāryas', 'ndāvana', 'ṣṭhira', 'ṣit', 'ṅkarṣaṇa',
                                         'ṣatriya', 'ṣatriyas', 'hākura', 'ṇḍavas', 'ṇyakaśipu', 'ṛṣis', 'ṛta', 'ṛtas',
                                         'ṣad', 'ṣads', 'ḍha', 'stha', 'sthas', 'bhūta', 'maya', 'mayī', 'devī'}
    
    # Or w2 is a single vowel completing a diacritic stem (like Kṛṣṇ + a, brāhmaṇ + a, guṇ + ā, etc.)
    if len(w2) == 1 and w2 in 'aāiīuūeoṛ':
        to_join.append(((w1, hyp, w2), cnt, w1 + w2))
    elif w1_lower in COMMON_WORDS and not is_diacritic_fragment:
        to_space.append(((w1, hyp, w2), cnt, f"{w1} {w2}"))
    else:
        # It's a broken word fragment!
        to_join.append(((w1, hyp, w2), cnt, w1 + w2))

print(f"Total pairs to JOIN (broken words healed): {len(to_join)} unique triples ({sum(c for _, c, _ in to_join)} total occurrences)")
print(f"Total pairs to KEEP SPACES (separate words): {len(to_space)} unique triples ({sum(c for _, c, _ in to_space)} total occurrences)")

print("\nSample to_join (broken words):")
for (w1, hyp, w2), cnt, res in to_join[:25]:
    print(f"  {cnt:3d}x: '{w1}' + '{hyp}' + '{w2}' -> '{res}'")

print("\nSample to_space (separate words):")
for (w1, hyp, w2), cnt, res in to_space[:25]:
    print(f"  {cnt:3d}x: '{w1}' + '{hyp}' + '{w2}' -> '{res}'")

conn.close()
