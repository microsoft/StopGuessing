# StopGuessing
A system for protecting password-based authentication systems from online-guessing attacks.

## Purpose
Services that employ passwords to authenticate users are subject to online-guessing attacks.
Attackers can pick a common password and try to login to the user's account with that password.
If services don't do anything to stop this attack, attackers can issue millions of guesses and compromise many accounts.
Some services block user accounts after a few failed guesses, but if attackers are trying to login to all user accounts
this will cause all users to be locked out.
Thus, more advanced systems to prevent online-guessing attacks block IP addresses engaged in guessing, rather than
the accounts targeted by guessers.

StopGuessing is a reference implementation of an IP reputation framework.
It provides two unique features not present in previous system.
First, StopGuessing identifies frequently-occuring passwords in failed login attempts to identify which passwords are being frequently guessed by attackers.
It can provide stronger protection to users whose passwords are among those being guessed frequently, and provide faster blocking to IP addresses that guess these passwords.
To detect frequently-occuring incorrect passwords, it uses a new data structure called a binomial ladder filter.
Second, StopGuessing is able to identify which login attempts have failed due to typos of the users' password,
and be less quick to conclude that an IP that submitted the typo is guessing than for a failure that is not caused by a typo.

For more information about the motivation for this approach, the underlying algorithms, and for simulations that measure the
efficacy of StopGuessing against different attacks, see the following papers:

The Binomial Ladder Filter: https://research.microsoft.com/...
StopGuessing: https://research.microsoft.com/...

## Project Structure



## Contributing
There are many opportunities to contribute to the StopGuessing project.
You might want to help the system use additional IP reputation information, or information about the geographic location or other features of IPs.
You might want to make it easier to use StopGuessing on other platforms.
You might want to port part or all of the code to be native to other languages.
You might want to build support for the binomial ladder filter into memory databases.
If you'd like to contribute, the best way to get started is to reach out to us at stopguessing@microsoft.com.

