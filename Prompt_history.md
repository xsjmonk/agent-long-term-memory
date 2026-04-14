Give a full design of this
1. Use harness way to make the control plain
```
Requirement
  ↓
Harness
  ├─ interpret requirement
  ├─ build structured retrieval input
  ├─ call MCP memory tools
  ├─ assemble memory pack
  ├─ generate execution plan
  ├─ dispatch to worker (by human copy and paste)
  └─ validate result (by human initialization prompt)
```
2. Accuracy of memory retrieval from database is provided and guranteed by MCP
3. Design the input and output between harness and MCP in details

Make design in a markdown. Do not show here and provide download link


# 
1. Do not do validation design
2. Do not design how to decopose knowledge articles into database elements. This will be in another design
3. Make sure querying long-term memory will get accurate contexts related to the task at hand. This needs a string ability to relate the task to the querying. Carefully design how to improve this ability
3.1 Design how to break the task descriptions into small chunks for queries
3.2 Design how to query database for precise relevance accuracy and it does not lose relevant contexts from database

Think carefullly for the design. Make design in a markdown. Do not show here and provide download link

# Decompose a requirement into chunks for query
Check this design. You should add this part in the design. The harness should be special designed to make sure a user requirement is decomposed into correct chunks for query. Note that the query cannot take long texts. So you must design it in a way for high accuracy, relevancy, etc. >

Think carefullly for the design. Make design in a markdown. Do not show here and provide download link

# Database embedding builder design
Now make a design for this embedding database builder as you have figured out. 
1. Use python
2. Make it for high accurate relevancy for the context quering
3. Make it compatible with the entire system
4. Hard requirement: the builder should figure out the categories such as best practice, prject history, anti-patterns, implementaiton, feature description, etc. 
4.1 The categories can be dynamic. The category might be in the meta data 
4.2. A piece of text for a certain category might be just a few sentences or an entire md.

Think carefullly for the design. Make design in a markdown. Do not show here and provide download link



# 
* harness_mcp_full_design introduces the project
* database_design describes database design
* embedding_builder_design is a component which builds actual database content. 

Now you should design prompts to make the embedding_builder_design. 

First, Review `database_design`
Requirement: 
1. It must fit into the system for context retrieval based on the user given task in the form of a prompt task description
2. It must provide high relevant context and accurate results
3. It must be extensible for contexts such as best practices, past code commits, lesson learnt, anti-patterns, etc. This context should be extensible in future. For example, code structure tracking may be added in future, when it is needed

If database design is changed, make design in a markdown. Do not show here and provide download link

# embedding_builder prompt
Design prompts to implement the embedding_builder_design. 
1. Use local llm. If llm does not exist, download it
2. Use a json config file for the builder. 
* Database connection username and password
* llm local path should be configured there. 
* The dir path for txt and md files. They are used to build database contents and embeddings, etc.
* The other configs should be placed in that config file. 

3. Give programming details m
4. Use conda environment
5. Use `uv` for python dependency management
6. Run one command to start for process `python agent_embedding_builder.py`

# embedding_builder
Check implementation `agent_embedding_builder_package.zip`. Does it fulfill requirements? 

In particular, check if this is done
* Json config file for database, path of knowledage base (md files and txt files)
* How to categorize knowledge base and how to define categories? Do I define something like a marddown file to describe categories. This will define categories and help generate embedding mata-data

If not, give prompt to fix it. Show the prompt in a download link. 
If the design should be revised, revise the design and show in a download link

# Code review prompt

Check implementation `agent_embedding_builder_package.zip`. Does it fulfill requirements design `embedding_builder_design`? 

Design prompt to fix it. The prompt should give all details include design decisions. Do not leave decisions to codex. 
Again, You should make a **strict** and **final** prompt. Make sure it makes the changes and fulfills the requirement in one round. If needed, you should make tests to drive it to finish until tests pass. 

Show revision prompt in a new download link

# MCP design
* harness_mcp_full_design introduces the project
* database_design describes database structure
* embedding_builder_design is a component which builds actual database content. It is already implemented. 

Now you should design full MCP. 

1. Make sure MCP server provides all needed tools
2. Make sure it can give high relevant content texts from database. 
3. Design the interface that can be used by harness for querying
4. Design the output formats that can be interpreted by harness. Harness uses this to build up quality design specification
5. Tech requirements:
* Use .net tech stack and make sure it reuses the latest framework
* Support AOT build
* Make configurations in the config file such as port, database connection, etc.
* Need a user friendly web interface to show the outputs to the agent harness. Maybe use signalR for streaming and make sure the page does not slow down when the logs are heavy after long time running. Simply trim the previous outputs to make browser responsive enough
* Make a log system. Rotate the log based on the file size. It can be 10MB. MAke this configurable in the app config

Make the design in the markdown. Show the download link. Do not show the content here.



# MCP prompt
* harness_mcp_full_design introduces the project
* database_design describes database design
* mcp_server_design is what you should focous on

Now you should design prompts to drive codex to do implemenation for mcp_server_design. 

First, you should give all programming details
* Classes, interfaces design
* Check if you need dependency injection and make it compatible with AOT
* Determine which libraries should be used.
* Key programming algorithmns. 
* Anything that will be needed for a decision making

Do not leave any technical details to codex. I find it designs things in a very narrow way and short-sighted.

Second, you should final and concrete and strict prompts. Show it in a download link

# 
Check implementation. Does it fulfill requirements design `mcp_server_design.md`? 

In particular, check whther mcp server can provide accurate search in the flow controlled by harness?

If not, design prompt to fix it. The prompt should give all details include design decisions. Do not leave decisions to codex. 
Again, You should make a **strict** and **final** prompt. Make sure it makes the changes and fulfills the requirement in one round. If needed, you should make tests to drive it to finish until tests pass. 

Also we have limited budget. Do it in a buget saving way.

Show revision prompt in a new download link



# Create scripts for mcp project
Create a `copy_src.ps1` in `C:\Docs\工作笔记\Hackthon\2026\mcp_server\Scripts`
1. Copy source code and projects. Keep the folder structure
2. Exclude bin and obj folders
3. It can be run in arbitrary folder. Copy the files in the current running folder. When the copy is done, pack them as a zip file
4. If the current running folder has the zip file, delete it first
5. Clean the temporary folder and files


Create a `build.ps1` in `C:\Docs\工作笔记\Hackthon\2026\mcp_server\Scripts`
1. Build the project in release mode. Build it to AOT
2. The artifacts should be built in `mcp_server/Release` folder
3. It can be run in arbitrary folder.


# 
Revise the mcp server design doc. Add the sematic search support by the api for generating compatible query You must consider completeness, correctness and acrateness in the harness flow. Have all in the design doc. Show download link


# review of embedding api design
Based on the mcp design, review the contract of embedding api design. If it iswhether the embedding API contract is strong enough for accurate semantic retrieval in the harness-controlled flow.

# embedding Api design code review
Check implementation. Does it fulfill api design? 

If not, design prompt to fix it. The prompt should give all details include design decisions. Do not leave decisions to codex. 
Again, You should make a **strict** and **final** prompt. Make sure it makes the changes and fulfills the requirement in one round. If needed, you should make tests to drive it to finish until tests pass. 

Also we have limited budget. Do it in a buget saving way.

Show revision prompt in a new download link
