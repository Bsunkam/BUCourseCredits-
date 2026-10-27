"""
Generates the SYNTHETIC reference data sized like the sample data the brief describes:

    ~150 BU courses for one program (Computer Science, with its supporting departments)
    ~200 courses from two other universities' public catalogues (Northeastern, NYU)
    ~100 past course-match decisions
    the labelled evaluation cases built from those decisions

Nothing here is real BU, Northeastern or NYU data: codes and titles are shaped like real catalogues but the
descriptions, outcomes and decisions are invented. Replace with the organisers' sample files when supplied.

    python src/database/scripts/generate_synthetic_data.py

Writes src/database/TransferCredit.Data/Seed/catalogue.json and src/agent/eval/labelled_cases.json.
Deterministic: the same script always produces the same files.
"""

import json
import random
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CATALOGUE_OUT = ROOT / "database" / "TransferCredit.Data" / "Seed" / "catalogue.json"
EVAL_OUT = ROOT / "agent" / "eval" / "labelled_cases.json"

PROGRAM = "Computer Science"
NEU = "Northeastern University"
NYU = "New York University"
BSCC = "Bay State Community College (synthetic)"
PCC = "Pacific Coast College (synthetic)"

# (code, title, owning program, requirement type for that program, description as topic phrases)
# Requirement types: "Major core", "Major elective", "Math and science requirement", "General elective".
BU = [
    # ---- Computer Science: core (the first nine match the original hand-made seed) ----
    ("CAS CS 111", "Introduction to Computer Science 1", PROGRAM, "Major core", ["programming and problem solving in Python", "variables, control flow and functions", "recursion", "lists and dictionaries", "basic algorithms and program design"]),
    ("CAS CS 112", "Introduction to Computer Science 2", PROGRAM, "Major core", ["object-oriented programming in Java", "data structures including linked lists, stacks, queues, trees, hash tables and graphs", "recursion", "analysis of algorithms"]),
    ("CAS CS 131", "Combinatoric Structures", PROGRAM, "Major core", ["discrete mathematics for computer science", "logic, sets, functions and relations", "proof techniques and induction", "counting and combinatorics", "graphs and trees"]),
    ("CAS CS 210", "Computer Systems", PROGRAM, "Major core", ["computer organisation and systems programming in C and assembly", "data representation", "memory and caches", "processes and linking", "the operating system interface"]),
    ("CAS CS 237", "Probability in Computing", PROGRAM, "Major core", ["probability theory for computer science", "random variables and distributions", "expectation and variance", "conditional probability and Bayes rule", "simulation and randomised algorithms"]),
    ("CAS CS 320", "Concepts of Programming Languages", PROGRAM, "Major core", ["programming language concepts", "syntax and semantics", "type systems", "functional programming", "interpreters, scope, binding and evaluation strategies"]),
    ("CAS CS 330", "Introduction to Analysis of Algorithms", PROGRAM, "Major core", ["design and analysis of algorithms", "asymptotic analysis", "divide and conquer and greedy algorithms", "dynamic programming", "graph algorithms and NP-completeness"]),
    ("CAS CS 332", "Elements of the Theory of Computation", PROGRAM, "Major core", ["theory of computation", "finite automata and regular languages", "context-free grammars and pushdown automata", "Turing machines", "decidability and computational complexity"]),
    ("CAS CS 350", "Fundamentals of Computing Systems", PROGRAM, "Major core", ["computing systems performance", "concurrency and synchronisation", "scheduling", "queueing models", "operating system fundamentals"]),
    # ---- Computer Science: electives and introductory ----
    ("CAS CS 101", "Introduction to Computing", PROGRAM, "General elective", ["computing concepts for non-majors", "hardware and software", "the internet", "spreadsheets", "a first look at programming"]),
    ("CAS CS 105", "Introduction to Databases and Data Mining", PROGRAM, "General elective", ["relational databases", "SQL queries", "data mining", "clustering and classification for non-majors"]),
    ("CAS CS 108", "Application Programming", PROGRAM, "General elective", ["building applications in Python", "files and text processing", "graphical user interfaces", "working with web data"]),
    ("CAS CS 132", "Geometric Algorithms", PROGRAM, "Major elective", ["linear algebra for computing", "vectors and matrices", "linear transformations", "eigenvalues", "applications to graphics and data"]),
    ("CAS CS 235", "Algebraic Algorithms", PROGRAM, "Major elective", ["number theory", "modular arithmetic", "groups and fields", "public-key cryptography and RSA"]),
    ("CAS CS 351", "Distributed Systems", PROGRAM, "Major elective", ["distributed computing", "remote procedure calls", "replication and consistency", "consensus", "fault tolerance"]),
    ("CAS CS 357", "Introduction to Information Security", PROGRAM, "Major elective", ["security threats and attacks", "cryptography basics", "authentication and access control", "network and web security"]),
    ("CAS CS 365", "Foundations of Data Science", PROGRAM, "Major elective", ["probability and statistics for data", "linear models", "statistical inference", "working with real datasets"]),
    ("CAS CS 391", "Mobile Application Development", PROGRAM, "Major elective", ["mobile application development", "Android and iOS user interfaces", "device sensors", "app lifecycle and publishing"]),
    ("CAS CS 392", "Rapid Software Prototyping", PROGRAM, "Major elective", ["rapid prototyping of web applications", "user feedback cycles", "frontend frameworks", "deployment"]),
    ("CAS CS 410", "Advanced Software Systems", PROGRAM, "Major elective", ["operating system internals", "kernels", "file systems", "virtual memory", "device drivers"]),
    ("CAS CS 411", "Software Engineering", PROGRAM, "Major elective", ["the software lifecycle", "requirements and design", "design patterns", "testing", "agile teams and version control"]),
    ("CAS CS 415", "Parallel Computing", PROGRAM, "Major elective", ["multicore programming with threads", "GPU programming", "message passing with MPI", "parallel algorithms"]),
    ("CAS CS 418", "Cloud Computing", PROGRAM, "Major elective", ["virtualisation", "containers", "distributed storage", "cloud services and scalability"]),
    ("CAS CS 422", "Game Development", PROGRAM, "Major elective", ["game engines", "physics simulation", "real-time graphics", "game design"]),
    ("CAS CS 440", "Introduction to Artificial Intelligence", PROGRAM, "Major elective", ["search and game playing", "logic and planning", "probabilistic reasoning", "introductory machine learning"]),
    ("CAS CS 445", "Computational Geometry", PROGRAM, "Major elective", ["convex hulls", "Voronoi diagrams and triangulations", "geometric search", "geometric algorithms"]),
    ("CAS CS 454", "Embedded Systems", PROGRAM, "Major elective", ["microcontrollers", "real-time systems", "sensors and actuators", "low-level C programming"]),
    ("CAS CS 455", "Computer Networks", PROGRAM, "Major elective", ["network layers", "TCP/IP", "routing", "congestion control", "socket programming"]),
    ("CAS CS 459", "Software Security", PROGRAM, "Major elective", ["buffer overflows", "secure coding", "static analysis", "fuzzing"]),
    ("CAS CS 460", "Introduction to Database Systems", PROGRAM, "Major elective", ["the relational model", "SQL", "normalisation", "indexing", "transactions and query processing"]),
    ("CAS CS 470", "Semantics of Programming Languages", PROGRAM, "Major elective", ["lambda calculus", "type theory", "operational semantics", "program verification"]),
    ("CAS CS 480", "Introduction to Computer Graphics", PROGRAM, "Major elective", ["the rendering pipeline", "geometric transformations", "shading and lighting", "rasterisation", "OpenGL"]),
    ("CAS CS 506", "Tools for Data Science", PROGRAM, "Major elective", ["data cleaning", "visualisation", "clustering", "regression", "Python data libraries"]),
    ("CAS CS 511", "Object-Oriented Software Principles", PROGRAM, "Major elective", ["object-oriented design", "design patterns in Java", "UML", "refactoring"]),
    ("CAS CS 523", "Deep Learning", PROGRAM, "Major elective", ["neural networks", "backpropagation", "convolutional networks", "recurrent networks", "optimisation for training"]),
    ("CAS CS 525", "Compiler Design", PROGRAM, "Major elective", ["lexical analysis", "parsing", "semantic analysis", "code generation", "compiler optimisation"]),
    ("CAS CS 530", "Advanced Algorithms", PROGRAM, "Major elective", ["randomised algorithms", "approximation algorithms", "network flow", "linear programming"]),
    ("CAS CS 535", "Complexity Theory", PROGRAM, "Major elective", ["complexity classes", "reductions", "NP-completeness", "space complexity", "randomised complexity"]),
    ("CAS CS 538", "Fundamentals of Cryptography", PROGRAM, "Major elective", ["encryption", "pseudorandomness", "hash functions", "digital signatures", "proofs of security"]),
    ("CAS CS 542", "Machine Learning", PROGRAM, "Major elective", ["supervised learning", "regression and classification", "support vector machines", "neural networks", "model evaluation"]),
    ("CAS CS 552", "Operating Systems", PROGRAM, "Major elective", ["processes and threads", "scheduling", "memory management", "file systems", "synchronisation"]),
    ("CAS CS 558", "Network Security", PROGRAM, "Major elective", ["firewalls", "intrusion detection", "TLS", "network attacks", "virtual private networks"]),
    ("CAS CS 561", "Advanced Database Systems", PROGRAM, "Major elective", ["query optimisation", "distributed databases", "NoSQL stores", "storage engines"]),
    ("CAS CS 565", "Data Mining", PROGRAM, "Major elective", ["frequent itemsets", "clustering", "classification", "dimensionality reduction", "mining large datasets"]),
    ("CAS CS 585", "Image and Video Computing", PROGRAM, "Major elective", ["image processing", "filtering and edge detection", "segmentation", "object recognition", "video analysis"]),
    ("CAS CS 591", "Robotics", PROGRAM, "Major elective", ["robot kinematics", "motion planning", "sensors", "feedback control"]),
    ("CAS CS 592", "Quantum Computing", PROGRAM, "Major elective", ["qubits", "quantum gates and circuits", "quantum algorithms", "Shor and Grover algorithms"]),
    ("CAS CS 595", "Human-Computer Interaction", PROGRAM, "Major elective", ["user-centred design", "usability testing", "interface prototyping", "accessibility"]),
    ("CAS CS 596", "Natural Language Processing", PROGRAM, "Major elective", ["text processing", "language models", "parsing", "word embeddings", "sentiment analysis"]),
    ("CAS CS 597", "Web Application Development", PROGRAM, "Major elective", ["HTML, CSS and JavaScript", "server-side frameworks", "REST APIs", "web application deployment"]),
    ("CAS CS 598", "Blockchain and Distributed Ledgers", PROGRAM, "Major elective", ["blockchains", "consensus protocols", "smart contracts", "cryptocurrency security"]),
    ("CAS CS 599", "Information Retrieval", PROGRAM, "Major elective", ["indexing", "ranking", "search engines", "evaluation of retrieval systems"]),
    ("CAS CS 509", "Software Testing", PROGRAM, "Major elective", ["unit and integration testing", "test-driven development", "coverage", "continuous integration"]),
    ("CAS CS 516", "Reinforcement Learning", PROGRAM, "Major elective", ["Markov decision processes", "value iteration", "policy gradients", "exploration"]),
    ("CAS CS 518", "Computational Biology", PROGRAM, "Major elective", ["sequence alignment", "genome assembly", "phylogenetics", "biological data analysis"]),
    ("CAS CS 548", "Formal Methods", PROGRAM, "Major elective", ["model checking", "temporal logic", "theorem proving", "specification"]),
    ("CAS CS 549", "Computer Architecture", PROGRAM, "Major elective", ["instruction set architecture", "pipelining", "memory hierarchy", "multiprocessors"]),
    ("CAS CS 556", "Visualization", PROGRAM, "Major elective", ["visual encoding", "interactive visualisation", "perception", "dashboard design"]),
    ("CAS CS 562", "Big Data Systems", PROGRAM, "Major elective", ["MapReduce", "Spark", "stream processing", "large-scale data pipelines"]),
    ("CAS CS 566", "Ethics of Computing", PROGRAM, "Major elective", ["privacy", "algorithmic fairness", "professional responsibility", "technology policy"]),
    # ---- Mathematics and statistics ----
    ("CAS MA 123", "Calculus I", "Mathematics", "Math and science requirement", ["limits and continuity", "derivatives", "applications of differentiation", "introduction to integrals", "the fundamental theorem of calculus"]),
    ("CAS MA 124", "Calculus II", "Mathematics", "Math and science requirement", ["techniques of integration", "applications of integrals", "sequences and series", "Taylor series", "differential equations"]),
    ("CAS MA 225", "Multivariate Calculus", "Mathematics", "Math and science requirement", ["vectors", "partial derivatives", "multiple integrals", "line and surface integrals", "Green, Stokes and divergence theorems"]),
    ("CAS MA 242", "Linear Algebra", "Mathematics", "Math and science requirement", ["systems of linear equations", "matrices", "vector spaces", "linear transformations", "determinants, eigenvalues and eigenvectors"]),
    ("CAS MA 113", "Elementary Statistics", "Mathematics", "General elective", ["descriptive statistics", "probability", "sampling distributions", "confidence intervals", "hypothesis tests"]),
    ("CAS MA 115", "Statistics I", "Mathematics", "Math and science requirement", ["data description", "probability models", "estimation", "hypothesis testing", "simple regression"]),
    ("CAS MA 116", "Statistics II", "Mathematics", "Math and science requirement", ["multiple regression", "analysis of variance", "nonparametric tests", "categorical data"]),
    ("CAS MA 121", "Calculus for the Life and Social Sciences I", "Mathematics", "General elective", ["functions and graphs", "derivatives", "optimisation", "exponential growth models"]),
    ("CAS MA 226", "Differential Equations", "Mathematics", "Math and science requirement", ["first-order equations", "linear systems", "Laplace transforms", "numerical methods", "modelling"]),
    ("CAS MA 293", "Discrete Mathematics", "Mathematics", "Math and science requirement", ["combinatorics", "recurrence relations", "generating functions", "graph theory"]),
    ("CAS MA 294", "Number Theory", "Mathematics", "Major elective", ["divisibility", "congruences", "prime numbers", "quadratic reciprocity"]),
    ("CAS MA 311", "Introduction to Analysis", "Mathematics", "Major elective", ["real numbers", "sequences and limits", "continuity", "rigorous proofs"]),
    ("CAS MA 381", "Probability", "Mathematics", "Math and science requirement", ["probability spaces", "random variables", "joint distributions", "limit theorems"]),
    ("CAS MA 411", "Advanced Calculus", "Mathematics", "Major elective", ["metric spaces", "uniform convergence", "multivariable differentiation", "implicit function theorem"]),
    ("CAS MA 442", "Linear Algebra II", "Mathematics", "Major elective", ["inner product spaces", "spectral theorem", "canonical forms", "applications"]),
    ("CAS MA 416", "Applied Regression Analysis", "Mathematics", "Major elective", ["linear regression", "model selection", "diagnostics", "generalised linear models"]),
    ("CAS MA 471", "Topology", "Mathematics", "Major elective", ["topological spaces", "continuity", "compactness", "connectedness"]),
    ("CAS MA 541", "Modern Algebra", "Mathematics", "Major elective", ["groups", "rings", "fields", "homomorphisms"]),
    ("CAS MA 555", "Numerical Analysis", "Mathematics", "Major elective", ["floating point error", "root finding", "interpolation", "numerical integration", "numerical linear algebra"]),
    ("CAS MA 575", "Linear Models", "Mathematics", "Major elective", ["least squares", "estimation theory", "analysis of variance", "regression inference"]),
    ("CAS MA 581", "Probability for Statistics", "Mathematics", "Major elective", ["moment generating functions", "transformations of random variables", "convergence", "the central limit theorem"]),
    ("CAS MA 589", "Computational Statistics", "Mathematics", "Major elective", ["simulation", "bootstrap methods", "Monte Carlo integration", "Markov chain Monte Carlo"]),
    ("CAS MA 109", "Precalculus", "Mathematics", "General elective", ["algebra review", "functions", "trigonometry", "exponential and logarithmic functions"]),
    ("CAS MA 120", "Applied Mathematics for Social Sciences", "Mathematics", "General elective", ["linear functions", "matrices", "basic probability", "applications to economics"]),
    ("CAS MA 330", "Mathematical Modelling", "Mathematics", "Major elective", ["dynamical systems", "difference equations", "stochastic models", "model validation"]),
    # ---- Natural sciences ----
    ("CAS PY 211", "General Physics I", "Physics", "Math and science requirement", ["kinematics", "Newton's laws", "work and energy", "momentum", "rotational motion with laboratory"]),
    ("CAS PY 212", "General Physics II", "Physics", "Math and science requirement", ["electric fields", "circuits", "magnetism", "waves and optics with laboratory"]),
    ("CAS PY 251", "Principles of Physics I", "Physics", "Math and science requirement", ["calculus-based mechanics", "oscillations", "gravitation", "laboratory measurement"]),
    ("CAS PY 252", "Principles of Physics II", "Physics", "Math and science requirement", ["calculus-based electricity and magnetism", "Maxwell's equations", "electromagnetic waves"]),
    ("CAS PY 313", "Modern Physics", "Physics", "General elective", ["special relativity", "quantum phenomena", "atomic structure", "nuclear physics"]),
    ("CAS CH 101", "General Chemistry I", "Chemistry", "Math and science requirement", ["atomic structure", "chemical bonding", "stoichiometry", "gases", "laboratory techniques"]),
    ("CAS CH 102", "General Chemistry II", "Chemistry", "Math and science requirement", ["chemical equilibrium", "acids and bases", "thermodynamics", "kinetics", "electrochemistry"]),
    ("CAS CH 203", "Organic Chemistry I", "Chemistry", "General elective", ["structure of organic molecules", "stereochemistry", "reaction mechanisms", "alkanes and alkenes"]),
    ("CAS CH 204", "Organic Chemistry II", "Chemistry", "General elective", ["aromatic compounds", "carbonyl chemistry", "synthesis", "spectroscopy"]),
    ("CAS BI 107", "Biology 1", "Biology", "Math and science requirement", ["evolution", "ecology", "biodiversity", "organismal biology with laboratory"]),
    ("CAS BI 108", "Biology 2", "Biology", "Math and science requirement", ["cell structure and function", "molecular biology", "genetics", "metabolism with laboratory"]),
    ("CAS BI 203", "Cell Biology", "Biology", "Major core", ["cell membranes", "organelles", "cell signalling", "cytoskeleton", "cell cycle and cell division"]),
    ("CAS BI 206", "Genetics", "Biology", "Major core", ["Mendelian inheritance", "gene expression", "mutation", "population genetics"]),
    ("CAS BI 213", "Intensive Cell Biology", "Biology", "Major elective", ["protein trafficking", "signal transduction", "cell motility", "research techniques"]),
    ("CAS BI 225", "Behavioural Biology", "Biology", "Major elective", ["animal behaviour", "neuroethology", "evolution of behaviour", "field methods"]),
    ("CAS BI 315", "Systems Physiology", "Biology", "Major elective", ["nervous system", "cardiovascular system", "respiration", "homeostasis"]),
    ("CAS ES 105", "Environmental Earth Science", "Earth and Environment", "General elective", ["plate tectonics", "climate systems", "natural hazards", "earth resources"]),
    ("CAS AS 101", "The Solar System", "Astronomy", "General elective", ["planets", "orbital motion", "telescopes", "the formation of the solar system"]),
    # ---- Economics ----
    ("CAS EC 101", "Introductory Microeconomic Analysis", "Economics", "Major core", ["supply and demand", "consumer choice", "production and costs", "market structures", "competition, monopoly and welfare"]),
    ("CAS EC 102", "Introductory Macroeconomic Analysis", "Economics", "Major core", ["national income", "unemployment and inflation", "economic growth", "money and banking", "fiscal and monetary policy"]),
    ("CAS EC 201", "Intermediate Microeconomics", "Economics", "Major core", ["consumer and producer theory with calculus", "general equilibrium", "game theory", "market failures"]),
    ("CAS EC 202", "Intermediate Macroeconomics", "Economics", "Major core", ["growth models", "business cycles", "open-economy macroeconomics", "stabilisation policy"]),
    ("CAS EC 203", "Empirical Economics 1", "Economics", "Major core", ["statistics for economics", "probability and sampling", "estimation", "hypothesis testing and simple regression"]),
    ("CAS EC 204", "Empirical Economics 2", "Economics", "Major core", ["multiple regression", "causal inference", "panel data", "econometric software"]),
    ("CAS EC 320", "Economics of Information and Technology", "Economics", "Major elective", ["network effects", "platform markets", "pricing of digital goods", "technology regulation"]),
    ("CAS EC 391", "Industrial Organisation", "Economics", "Major elective", ["oligopoly", "strategic behaviour", "antitrust", "market power"]),
    # ---- Psychology ----
    ("CAS PS 101", "General Psychology", "Psychology", "Major core", ["biological bases of behaviour", "perception", "learning and memory", "development and personality", "social behaviour and psychological disorders"]),
    ("CAS PS 211", "Introduction to Experimental Psychology", "Psychology", "Major core", ["research methods in psychology", "experimental design", "measurement and statistics", "ethics and scientific writing"]),
    ("CAS PS 241", "Developmental Psychology", "Psychology", "Major elective", ["human development from infancy through adolescence", "cognitive development", "social and emotional development", "biological development"]),
    ("CAS PS 222", "Biological Psychology", "Psychology", "Major elective", ["neurons and neurotransmitters", "brain structure", "sensation", "sleep and motivation"]),
    ("CAS PS 231", "Social Psychology", "Psychology", "Major elective", ["attitudes", "social influence", "group behaviour", "prejudice"]),
    ("CAS PS 234", "Cognitive Psychology", "Psychology", "Major elective", ["attention", "memory systems", "language", "problem solving and decision making"]),
    ("CAS PS 261", "Abnormal Psychology", "Psychology", "Major elective", ["classification of disorders", "anxiety and mood disorders", "treatment approaches", "research on psychopathology"]),
    ("CAS PS 371", "Human-Computer Interaction Psychology", "Psychology", "Major elective", ["human perception and interfaces", "cognitive load", "usability", "experimental evaluation of interfaces"]),
    # ---- Writing, humanities and social sciences ----
    ("CAS WR 120", "First-Year Writing Seminar", "Writing", "General elective", ["academic writing", "argument and evidence", "revision", "critical reading"]),
    ("CAS WR 150", "Writing, Research and Inquiry", "Writing", "General elective", ["research writing", "using sources", "argumentative essays", "oral presentation"]),
    ("CAS WR 415", "Technical Writing", "Writing", "General elective", ["technical documentation", "writing for engineers", "instructions and reports", "document design"]),
    ("CAS PH 100", "Introduction to Philosophy", "Philosophy", "General elective", ["knowledge and belief", "mind and body", "free will", "ethics"]),
    ("CAS PH 160", "Reasoning and Argumentation", "Philosophy", "General elective", ["propositional logic", "argument analysis", "fallacies", "formal proofs"]),
    ("CAS PH 251", "Ethics", "Philosophy", "General elective", ["moral theories", "utilitarianism", "deontology", "applied ethics"]),
    ("CAS PH 360", "Symbolic Logic", "Philosophy", "General elective", ["predicate logic", "natural deduction", "completeness", "modal logic"]),
    ("CAS SO 100", "Principles of Sociology", "Sociology", "General elective", ["social structure", "culture", "inequality", "institutions"]),
    ("CAS SO 215", "Sociology of Technology", "Sociology", "General elective", ["technology and society", "digital divide", "surveillance", "work and automation"]),
    ("CAS AN 101", "Introduction to Cultural Anthropology", "Anthropology", "General elective", ["culture", "kinship", "ritual", "ethnographic methods"]),
    ("CAS HI 151", "The United States to 1865", "History", "General elective", ["colonial America", "the Revolution", "the early republic", "slavery and the Civil War"]),
    ("CAS HI 152", "The United States since 1865", "History", "General elective", ["Reconstruction", "industrialisation", "world wars", "civil rights"]),
    ("CAS HI 175", "World History", "History", "General elective", ["early civilisations", "empires", "global trade", "colonialism"]),
    ("CAS EN 120", "Introduction to Literature", "English", "General elective", ["close reading", "poetry", "fiction", "drama"]),
    ("CAS EN 220", "Shakespeare", "English", "General elective", ["Shakespeare's plays", "Elizabethan theatre", "performance", "literary criticism"]),
    ("CAS PO 111", "Introduction to American Politics", "Political Science", "General elective", ["the constitution", "Congress and the presidency", "elections", "public opinion"]),
    ("CAS PO 151", "Introduction to International Relations", "Political Science", "General elective", ["the state system", "war and peace", "international organisations", "global economy"]),
    ("COM CM 180", "Understanding Media", "Communication", "General elective", ["mass media", "media effects", "advertising", "digital media"]),
    ("COM CO 201", "Introduction to Communication Writing", "Communication", "General elective", ["news writing", "public relations writing", "editing", "media ethics"]),
    ("CAS AH 111", "Survey of Western Art", "Art History", "General elective", ["ancient art", "Renaissance", "modern art", "visual analysis"]),
    ("CFA MU 101", "Fundamentals of Music", "Music", "General elective", ["notation", "scales and intervals", "rhythm", "ear training"]),
    ("CAS LS 111", "First-Semester Spanish", "Languages", "General elective", ["Spanish pronunciation", "basic grammar", "everyday conversation", "Hispanic cultures"]),
    ("CAS LS 112", "Second-Semester Spanish", "Languages", "General elective", ["Spanish past tenses", "reading short texts", "conversation", "writing paragraphs"]),
    ("CAS LF 111", "First-Semester French", "Languages", "General elective", ["French pronunciation", "basic grammar", "everyday conversation", "francophone cultures"]),
    ("CAS LF 112", "Second-Semester French", "Languages", "General elective", ["French past tenses", "reading short texts", "conversation", "writing paragraphs"]),
    ("CAS LC 111", "First-Semester Chinese", "Languages", "General elective", ["Mandarin pronunciation and tones", "characters", "basic grammar", "conversation"]),
    # ---- Engineering service courses ----
    ("ENG EK 125", "Introduction to Programming for Engineers", "Engineering", "General elective", ["MATLAB programming", "numerical problem solving", "plotting data", "engineering applications"]),
    ("ENG EK 131", "Introduction to Engineering", "Engineering", "General elective", ["the engineering design process", "teamwork", "prototyping", "technical communication"]),
    ("ENG EC 311", "Introduction to Logic Design", "Engineering", "Major elective", ["Boolean algebra", "combinational circuits", "sequential circuits", "hardware description languages"]),
    ("ENG EC 327", "Introduction to Software Engineering", "Engineering", "Major elective", ["C++ programming", "software design", "debugging", "team software projects"]),
    ("ENG EC 381", "Probability in Electrical and Computer Engineering", "Engineering", "Major elective", ["probability for engineers", "random processes", "estimation", "signal noise"]),
    ("ENG EC 401", "Signals and Systems", "Engineering", "Major elective", ["continuous and discrete signals", "Fourier analysis", "linear systems", "sampling"]),
    ("CAS CS 103", "Computers and Society", PROGRAM, "General elective", ["social impact of computing", "privacy", "intellectual property", "the digital economy"]),
    ("CAS CS 113", "Introduction to Computer Science with Applications", PROGRAM, "General elective", ["programming in Python for scientists", "data analysis", "simulation", "plotting"]),
    ("CAS CS 116", "Computer Science for Educators", PROGRAM, "General elective", ["teaching programming", "block-based languages", "computational thinking", "curriculum design"]),
    ("CAS CS 400", "Senior Design Project", PROGRAM, "Major elective", ["team software project", "requirements to delivery", "project management", "technical presentation"]),
]

ORIGINAL_CHECK = {"CAS CS 237", "CAS EC 201", "CAS EC 203", "CAS PS 211", "CAS BI 203", "CAS MA 242"}
# The 23 courses of the original hand-made seed keep their bu.edu links; generated courses get none, because a
# shaped-like-real code may not exist and a dead or wrong link would look like a verified source.
ORIGINAL_23 = {"CAS CS 111", "CAS CS 112", "CAS CS 131", "CAS CS 210", "CAS CS 237", "CAS CS 320", "CAS CS 330",
               "CAS CS 332", "CAS CS 350", "CAS EC 101", "CAS EC 102", "CAS EC 201", "CAS EC 203", "CAS PS 101",
               "CAS PS 211", "CAS PS 241", "CAS BI 107", "CAS BI 108", "CAS BI 203", "CAS MA 123", "CAS MA 124",
               "CAS MA 225", "CAS MA 242"}
BU_ONLY = {"CAS CS 320", "CAS CS 332", "CAS CS 350"}

# Topic synonyms: another institution words the same topic differently, so matching is not a string copy.
SYNONYMS = {
    "Python": "a high-level language", "Java": "an object-oriented language", "recursion": "recursive thinking",
    "analysis of algorithms": "algorithm efficiency", "data structures": "data organisation",
    "graphs and trees": "trees and networks", "dynamic programming": "memoisation techniques",
    "asymptotic analysis": "big-O notation", "the relational model": "relational data modelling",
    "neural networks": "artificial neural models", "routing": "packet forwarding",
    "derivatives": "rates of change", "matrices": "matrix algebra", "probability": "chance and likelihood",
    "laboratory": "lab sessions", "hypothesis testing": "significance testing", "clustering": "grouping methods",
}

# Courses another institution teaches that BU has no direct course for: elective credit or no credit.
NO_MATCH_TOPICS = [
    ("Co-op Preparation Seminar", ["resume writing", "interview skills", "workplace norms"], "NoCredit"),
    ("College Success Strategies", ["study skills", "time management", "campus resources"], "NoCredit"),
    ("Intermediate Algebra", ["linear equations", "factoring polynomials", "graphing", "word problems"], "NoCredit"),
    ("Video Game Narrative", ["storytelling in games", "character arcs", "branching dialogue"], "ElectiveCredit"),
    ("Music Technology", ["digital audio workstations", "MIDI", "sound synthesis"], "ElectiveCredit"),
    ("Urban Ecology", ["cities as ecosystems", "green infrastructure", "urban wildlife"], "ElectiveCredit"),
    ("Entrepreneurship", ["startup formation", "business models", "venture financing"], "ElectiveCredit"),
    ("Sports Analytics", ["performance metrics", "sabermetrics", "player evaluation"], "ElectiveCredit"),
    ("Documentary Film", ["documentary history", "interview technique", "editing a short film"], "ElectiveCredit"),
    ("Marine Biology", ["ocean ecosystems", "marine invertebrates", "coral reefs"], "ElectiveCredit"),
    ("Public Speaking", ["speech preparation", "delivery", "persuasive speaking"], "ElectiveCredit"),
    ("Introduction to Accounting", ["financial statements", "double-entry bookkeeping", "accruals"], "ElectiveCredit"),
    ("Game Theory for Everyone", ["strategic games", "Nash equilibrium", "auctions"], "ElectiveCredit"),
    ("Digital Photography", ["exposure", "composition", "photo editing"], "ElectiveCredit"),
    ("Cybersecurity Policy", ["cyber law", "national security", "incident disclosure"], "ElectiveCredit"),
    ("Information Design", ["typography", "layout", "information graphics"], "ElectiveCredit"),
    ("Health Informatics", ["electronic health records", "health data standards", "clinical decision support"], "ElectiveCredit"),
    ("Linguistics", ["phonology", "syntax", "language acquisition"], "ElectiveCredit"),
    ("Climate Policy", ["emissions targets", "carbon pricing", "international agreements"], "ElectiveCredit"),
    ("Creative Coding", ["generative art", "interactive installations", "Processing sketches"], "ElectiveCredit"),
]

NEU_DEPTS = {"CAS CS": "CS", "CAS MA": "MATH", "CAS PY": "PHYS", "CAS CH": "CHEM", "CAS BI": "BIOL", "CAS EC": "ECON",
             "CAS PS": "PSYC", "CAS WR": "ENGW", "CAS PH": "PHIL", "CAS SO": "SOCL", "CAS AN": "ANTH",
             "CAS HI": "HIST", "CAS EN": "ENGL", "CAS PO": "POLS", "COM CM": "COMM", "COM CO": "COMM",
             "CAS AH": "ARTH", "CFA MU": "MUSC", "CAS LS": "SPNS", "CAS LF": "FRNH", "CAS LC": "CHNS",
             "ENG EK": "GE", "ENG EC": "EECE", "CAS ES": "ENVR", "CAS AS": "PHYS"}
NYU_DEPTS = {"CAS CS": "CSCI-UA", "CAS MA": "MATH-UA", "CAS PY": "PHYS-UA", "CAS CH": "CHEM-UA", "CAS BI": "BIOL-UA",
             "CAS EC": "ECON-UA", "CAS PS": "PSYCH-UA", "CAS WR": "EXPOS-UA", "CAS PH": "PHIL-UA",
             "CAS SO": "SOC-UA", "CAS AN": "ANTH-UA", "CAS HI": "HIST-UA", "CAS EN": "ENGL-UA",
             "CAS PO": "POL-UA", "COM CM": "MCC-UE", "COM CO": "MCC-UE", "CAS AH": "ARTH-UA",
             "CFA MU": "MUSIC-UA", "CAS LS": "SPAN-UA", "CAS LF": "FREN-UA", "CAS LC": "EAST-UA",
             "ENG EK": "ENGR-UH", "ENG EC": "ECE-UY", "CAS ES": "ENVST-UA", "CAS AS": "PHYS-UA"}

# Hand-authored mappings from the original seed: kept exactly (the secondary Northeastern demo case uses them).
HAND_EQUIVALENCIES = [
    (NEU, "CS 2500", "Fundamentals of Computer Science 1", PROGRAM, "BuEquivalent", "CAS CS 111", "Yes"),
    (NEU, "MATH 1341", "Calculus 1 for Science and Engineering", PROGRAM, "BuEquivalent", "CAS MA 123", "Yes"),
    (NEU, "PSYC 1101", "Foundations of Psychology", PROGRAM, "BuEquivalent", "CAS PS 101", "Check"),
    (NEU, "CS 1800", "Discrete Structures", PROGRAM, "BuEquivalent", "CAS CS 131", "Yes"),
    (NYU, "CSCI-UA 101", "Introduction to Computer Science", PROGRAM, "BuEquivalent", "CAS CS 111", "Yes"),
    (NYU, "MATH-UA 121", "Calculus I", "Mathematics", "BuEquivalent", "CAS MA 123", "Yes"),
    (NYU, "ECON-UA 1", "Introduction to Macroeconomics", "Economics", "BuEquivalent", "CAS EC 102", "Check"),
    # Brief's six-course scenario: the three existing equivalencies and the quarter-system course.
    (BSCC, "CSC 110", "Introduction to Programming", PROGRAM, "BuEquivalent", "CAS CS 111", "Yes"),
    (BSCC, "MAT 210", "Calculus I", PROGRAM, "BuEquivalent", "CAS MA 123", "Yes"),
    (BSCC, "PSY 101", "Introduction to Psychology", PROGRAM, "BuEquivalent", "CAS PS 101", "Yes"),
    (PCC, "CS 140", "Data Structures and Algorithms", PROGRAM, "BuEquivalent", "CAS CS 112", "Yes"),
]

# Demo courses: present in the public catalogues, never given a past decision (they must stay "maybe").
RESERVED = {(NEU, "CS 2510"), (NEU, "CS 3800"), (BSCC, "CSC 215"), (BSCC, "CSC 230")}

HAND_CATALOGUE = [
    (NEU, "CS 2500", "Fundamentals of Computer Science 1", 4, ["systematic program design", "functional programming in a teaching language", "data definitions and recursion", "testing"]),
    (NEU, "MATH 1341", "Calculus 1 for Science and Engineering", 4, ["limits", "derivatives and their applications", "integrals", "the fundamental theorem of calculus"]),
    (NEU, "PSYC 1101", "Foundations of Psychology", 4, ["the brain and behaviour", "perception and learning", "memory", "personality and social behaviour"]),
    (NEU, "CS 1800", "Discrete Structures", 4, ["logic and proofs", "sets and relations", "counting", "graphs"]),
    (NEU, "CS 2510", "Fundamentals of Computer Science 2", 4, ["class-based program design in Java", "data structures", "abstraction", "testing"]),
    (NEU, "CS 3800", "Theory of Computation", 4, ["automata", "formal languages", "Turing machines", "computability and complexity"]),
    (NYU, "CSCI-UA 101", "Introduction to Computer Science", 4, ["programming in Python", "control structures", "functions", "lists and basic algorithms"]),
    (NYU, "MATH-UA 121", "Calculus I", 4, ["limits", "derivatives", "applications of derivatives", "integration"]),
    (NYU, "ECON-UA 1", "Introduction to Macroeconomics", 4, ["national income", "inflation and unemployment", "monetary policy", "fiscal policy"]),
    (BSCC, "CSC 110", "Introduction to Programming", 3, ["programming in Python", "variables, loops and functions", "lists and dictionaries", "problem solving"]),
    (BSCC, "MAT 210", "Calculus I", 4, ["limits and continuity", "derivatives", "applications of differentiation", "definite integrals"]),
    (BSCC, "PSY 101", "Introduction to Psychology", 3, ["biological bases of behaviour", "learning and memory", "development", "psychological disorders"]),
    (BSCC, "CSC 215", "Discrete Structures for Computing", 3, ["propositional logic", "proof techniques", "sets and functions", "counting", "graphs"]),
    (BSCC, "CSC 230", "Computer Organization and C Programming", 3, ["C programming", "binary data representation", "memory and pointers", "assembly language"]),
    (BSCC, "ENG 101", "English Composition I", 3, ["academic essays", "revision", "critical reading"]),
    (BSCC, "SOC 101", "Introduction to Sociology", 3, ["culture", "social structure", "inequality"]),
    (BSCC, "MAT 095", "Developmental Mathematics", 3, ["arithmetic review", "basic algebra", "word problems"]),
    (BSCC, "HIS 101", "United States History I", 3, ["colonial America", "the Revolution", "the Civil War"]),
    (PCC, "CS 140", "Data Structures and Algorithms", 4, ["object-oriented programming", "linked lists, stacks and queues", "trees and hash tables", "algorithm analysis"]),
    (PCC, "MATH 3A", "Calculus I", 4, ["limits", "derivatives", "applications of derivatives"]),
]


def sentence(phrases: list[str]) -> str:
    if len(phrases) == 1:
        return phrases[0][0].upper() + phrases[0][1:] + "."
    text = ", ".join(phrases[:-1]) + " and " + phrases[-1]
    return text[0].upper() + text[1:] + "."


def outcomes(phrases: list[str]) -> str:
    verbs = ["Explain", "Apply", "Analyse", "Use", "Evaluate"]
    return " ".join(f"{verbs[i % len(verbs)]} {p}." for i, p in enumerate(phrases))


def reword(phrases: list[str], rng: random.Random) -> list[str]:
    out = []
    for p in phrases:
        for word, alt in SYNONYMS.items():
            if word in p and rng.random() < 0.5:
                p = p.replace(word, alt)
        out.append(p)
    rng.shuffle(out)
    # Drop one topic sometimes, so a course covers most but not all of the BU course.
    if len(out) > 3 and rng.random() < 0.4:
        out.pop()
    return out


def course_number(prefix: str, bu_code: str, style: str, used: set[str]) -> str:
    n = int(bu_code.split()[-1])
    if style == "neu":
        base = 1000 + n * 10 if n < 200 else 2000 + (n % 200) * 10 if n < 400 else 3000 + (n % 400) * 10
        base = min(base, 4990)
    else:
        base = n
    code = f"{prefix} {base}"
    while code in used:
        base += 1
        code = f"{prefix} {base}"
    used.add(code)
    return code


def build():
    rng = random.Random(20261027)
    bu_by_code = {b[0]: b for b in BU}

    bu_courses = [
        {"code": code, "title": title, "program": program, "requirementType": req, "credits": 4,
         # Generated courses are unverified by definition; one in nine is still marked Check to exercise BR-12.
         "verified": "Check" if code in ORIGINAL_CHECK or (code not in ORIGINAL_23 and i % 9 == 0) else "Yes",
         "sourceUrl": f"https://www.bu.edu/academics/{code.split()[0].lower()}/courses/{code.lower().replace(' ', '-')}/"
                      if code in ORIGINAL_23 else None,
         "description": sentence(phrases), "learningOutcomes": outcomes(phrases)}
        for i, (code, title, program, req, phrases) in enumerate(BU)
    ]

    # ---------- other institutions' public catalogues ----------
    catalogue = []
    used = {(inst, code) for inst, code, *_ in HAND_CATALOGUE}
    for inst, code, title, credits, phrases in HAND_CATALOGUE:
        catalogue.append({"institution": inst, "code": code, "title": title, "credits": credits,
                          "description": sentence(phrases), "learningOutcomes": outcomes(phrases), "_bu": None})

    decisions = []
    for inst, depts, style in ((NEU, NEU_DEPTS, "neu"), (NYU, NYU_DEPTS, "nyu")):
        codes_used = {c for i, c in used if i == inst}
        chosen = [b for b in BU if b[0] not in BU_ONLY]
        rng.shuffle(chosen)
        for code, title, program, req, phrases in chosen[:85]:
            prefix = depts[" ".join(code.split()[:2])]
            src_code = course_number(prefix, code, style, codes_used)
            if (inst, src_code) in RESERVED:
                continue
            src_title = title.replace("Introduction to", rng.choice(["Introduction to", "Foundations of", "Principles of"]))
            words = reword(phrases, rng)
            catalogue.append({"institution": inst, "code": src_code, "title": src_title, "credits": 4,
                              "description": sentence(words), "learningOutcomes": outcomes(words), "_bu": code})
        for topic, phrases, outcome in NO_MATCH_TOPICS[: 15 if inst == NEU else 20]:
            prefix = "INSH" if inst == NEU else "CORE-UA"
            src_code = course_number(prefix, f"X {rng.randint(100, 499)}", style, codes_used)
            catalogue.append({"institution": inst, "code": src_code, "title": topic, "credits": 4 if outcome != "NoCredit" else 2,
                              "description": sentence(phrases), "learningOutcomes": outcomes(phrases), "_bu": None,
                              "_outcome": outcome})

    # ---------- ~100 past decisions (evaluator outcomes) ----------
    hand_keys = {(e[0], e[1]) for e in HAND_EQUIVALENCIES}
    pool = [c for c in catalogue if c["institution"] in (NEU, NYU) and (c["institution"], c["code"]) not in hand_keys
            and (c["institution"], c["code"]) not in RESERVED]
    matched = [c for c in pool if c["_bu"]]
    unmatched = [c for c in pool if not c["_bu"] and c.get("_outcome")]
    rng.shuffle(matched)
    picked = matched[:68] + unmatched[:25]
    for c in picked:
        bu = c["_bu"]
        if bu:
            outcome = "BuEquivalent"
        else:
            outcome = c["_outcome"]
        decisions.append((c["institution"], c["code"], c["title"], PROGRAM, outcome, bu,
                          "Check" if bu and bu_by_code[bu][0] in ORIGINAL_CHECK else "Yes"))

    equivalencies = [
        {"institution": i, "sourceCode": c, "sourceTitle": t, "program": p, "outcome": o, "buCourseCode": b, "verified": v}
        for i, c, t, p, o, b, v in HAND_EQUIVALENCIES + decisions
    ]

    data = {
        "_note": "SYNTHETIC reference data generated by src/database/scripts/generate_synthetic_data.py, sized like the "
                 "brief's sample data (~150 BU courses for one program, ~200 courses from two universities, ~100 past "
                 "decisions). Codes look like real catalogues; descriptions, outcomes and decisions are invented. "
                 "Not official BU equivalencies. Replace with the organisers' sample files when supplied.",
        "institutions": [
            {"name": NEU, "creditSystem": "Semester", "catalogueSource": "https://catalog.northeastern.edu/ (synthetic copy)"},
            {"name": NYU, "creditSystem": "Semester", "catalogueSource": "https://bulletins.nyu.edu/ (synthetic copy)"},
            {"name": BSCC, "creditSystem": "Semester", "catalogueSource": "Synthetic community college"},
            {"name": PCC, "creditSystem": "Quarter", "catalogueSource": "Synthetic quarter-system college"},
        ],
        "buCourses": bu_courses,
        "programRules": [
            {"program": PROGRAM, "buCourseCode": code, "ruleType": "MustBeTakenAtBu",
             "source": f"Workbook Programs sheet: {code.replace('CAS ', '')} must be taken at BU (CS program)"}
            for code in sorted(BU_ONLY)
        ],
        "sourceCatalogue": [{k: v for k, v in c.items() if not k.startswith("_")} for c in catalogue],
        "equivalencies": equivalencies,
    }
    CATALOGUE_OUT.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    # ---------- labelled evaluation cases: one per past decision ----------
    by_key = {(c["institution"], c["code"]): c for c in catalogue}
    cases = []
    for inst, code, title, program, outcome, bu, _ in decisions:
        src = by_key[(inst, code)]
        cases.append({
            "courseId": f"eval-{inst.split()[0].lower()}-{code.replace(' ', '-').lower()}",
            "program": program,
            "sourceCourse": {"institution": inst, "code": code, "title": title, "credits": src["credits"],
                             "creditSystem": "semester", "grade": "B", "term": "Fall 2025",
                             "description": src["description"], "learningOutcomes": src["learningOutcomes"]},
            # The catalogue entry stands in for the syllabus: the brief's past decisions were made on syllabi.
            "syllabusText": f"{src['description']} Learning outcomes: {src['learningOutcomes']}",
            "policyFlags": {"convertedCredits": src["credits"], "quarterConversionApplied": False,
                            "gradeBelowMinimum": False, "minimumGrade": "C",
                            "buOnlyCourses": sorted(BU_ONLY), "rulesVersion": "r-001"},
            "promptVersion": "p-001",
            "cacheKey": f"eval:{inst}:{code}",
            "expectedOutcome": outcome,
            "expectedBuCourse": bu,
        })
    EVAL_OUT.write_text(json.dumps(cases, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print(f"BU courses: {len(bu_courses)} ({sum(1 for b in bu_courses if b['program'] == PROGRAM)} CS)")
    print(f"Other-university catalogue courses: {len(catalogue)} "
          f"(NEU {sum(c['institution'] == NEU for c in catalogue)}, NYU {sum(c['institution'] == NYU for c in catalogue)})")
    print(f"Past decisions: {len(decisions)} generated + {len(HAND_EQUIVALENCIES)} hand-made mappings")
    print(f"Eval cases: {len(cases)} -> {EVAL_OUT}")


if __name__ == "__main__":
    build()
