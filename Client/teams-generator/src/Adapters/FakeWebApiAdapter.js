export async function getInitialConfig() {
    return {
        config: {
            numberOfTeams: 3,
            shirtsColors: [
                "red", "orange", "purple", "blue", "blue", "green"
            ]
        },
        algos: [
            {
                algoName: "BackToForth",
                description: "this is back to forth algo",
                displayName: "Back To Forth",
                algoKey: 0,
                playerProperties: [
                    {
                        name: "Name",
                        type: "text",
                        showInClient: true
                    },
                    {
                        name: "Rank",
                        type: "number",
                        showInClient: true
                    }
                ]
            },
            {
                algoName: "skillwise",
                description: "this is skillwise algo",
                displayName: "SkillWise",
                algoKey: 1,
                playerProperties: [
                    {
                        name: "Name",
                        type: "text",
                        showInClient: true
                    },
                    {
                        name: "attack",
                        type: "number",
                        showInClient: true
                    },
                    {
                        name: "defence",
                        type: "number",
                        showInClient: true
                    },
                    {
                        name: "stamina",
                        type: "number",
                        showInClient: true
                    },
                    {
                        name: "passing",
                        type: "number",
                        showInClient: true
                    },
                    {
                        name: "leadership",
                        type: "number",
                        showInClient: true
                    }
                ]
            }
        ]
    }
}


export async function getTeams(algoType, players, config){
    return {
        teams: [
            {
                teamSymbol: '🟥',
                color: 'Red',
                teamId: 1,
                players: [
                    {
                        name: 'Dor Kronzilber'
                    },
                    {
                        name: 'Idan Nicolet'
                    },
                    {
                        name: 'Tal Zelnik'
                    },
                    {
                        name: 'Aviv Koren'
                    },
                    {
                        name: 'Ben Elkayam'
                    },
                ]
            },
            {
                teamSymbol: '🟩',
                color: 'Green',
                teamId: 2,
                players: [
                    {
                        name: 'Or Raif'
                    },
                    {
                        name: 'Lior Amar'
                    },
                    {
                        name: 'Yossi Tamir'
                    },
                    {
                        name: 'Benny Assa'
                    },
                    {
                        name: 'Guy Barel'
                    },
                ]
            },
            {
                teamSymbol: '🟨',
                color: 'Yellow',
                teamId: 3,
                players: [
                    {
                        name: 'Elad Peleg'
                    },
                    {
                        name: 'Avi Goldberg'
                    },
                    {
                        name: 'Gal Gan'
                    },
                    {
                        name: 'Daniel Krasny'
                    },
                    {
                        name: 'Omer Shuali'
                    },
                ]
            }
            

        ]
    }
}


export async function getResultString(teams){
    const resposne = await fetch(`https://localhost:7236/Teams/PostResultString`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ teams}),
    })
    return resposne.json()
}

