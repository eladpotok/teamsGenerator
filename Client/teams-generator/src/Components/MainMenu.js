import { useContext, useEffect, useState } from "react";
import { getInitialAlgoConfig, getTeams } from '../Adapters/DB/WebApiAdapter'
import { Button, Card, Descriptions, Modal, Tabs,Tooltip,message } from 'antd';
import PlayersTable from "./PlayersTable";
import Teams from "./Teams";
import { TeamsContext } from "../Store/TeamsContext";
import Config from "./Config";
import { QuestionCircleOutlined, QuestionCircleTwoTone } from "@ant-design/icons";
import { ConfigurationContext } from "../Store/ConfigurationContext";
import { PlayersContext } from "../Store/PlayersContext";

function MainMenu(props) {

    const teamsContext = useContext(TeamsContext);
    const configContext = useContext(ConfigurationContext);
    const playersContext = useContext(PlayersContext);
    const [messageApi, contextHolder] = message.useMessage();

    const [teamsModalOpened, setTeamsModalOpened] = useState(false)

    async function generateTeamsHandler() {
        const selectedConfig = configContext.getSelectedConfig();
        if (selectedConfig.shirtsColors.length < 3) {
            messageApi.open({
                type: 'error',
                content: 'Must choose 3 shirts colors at least',
              });
            return;
        }
        const getTeamsResponse = await getTeams(configContext.algo, playersContext.players, configContext.getSelectedConfig())
        teamsContext.setTeams(getTeamsResponse.teams)
        teamsContext.setResultAsText(getTeamsResponse.teamsResultAsCopyText)
        setTeamsModalOpened(true)
    }

    function tabChangedHandler(e) {
        configContext.setAlgo(e)
    }

    useEffect(() => {
        (async () => {
            if (!configContext.config) {
                let initialAlgoConfig = await getInitialAlgoConfig();
                configContext.setConfig(initialAlgoConfig);
                configContext.setAlgo(initialAlgoConfig.algos[0].algoKey)
            }
        })()
    }, [configContext.config])


    return (
        <div>
            {contextHolder}

            <div>
                <div style={{ display: 'flex', justifyContent: 'flex-start', flexDirection: 'row' }}>
                    <Config />
                    <Card style={{ height: '100%', flex: 1, marginLeft: '4px' }} title="Algorithms" >
                        <QuestionCircleOutlined />  <label style={{ color: "gray", marginLeft: '4px' }}>Here are the most common algorithms for football teams creation. Choose your algorithm and add the players</label>
                        {configContext.config && <Tabs onChange={tabChangedHandler} style={{ margin: '10px' }} items={getTabsToShow(configContext.config.algos, generateTeamsHandler)} />}
                    </Card>
                </div>



            </div>
            <Modal onCancel={() => { setTeamsModalOpened(false) }} width={1000} open={teamsModalOpened} footer={[]}>
                {teamsContext.teams && <Teams onShiffleClicked={generateTeamsHandler} />}
            </Modal>

            <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-end', 'justifyContent': 'flex-end' }}>

                <div style={{ display: 'flex', 'flex-direction': 'row', 'align-items': 'flex-start', 'justifyContent': 'flex-start' }}>

                { teamsContext.teams &&   <Button  onClick={()=>{setTeamsModalOpened(true)}} type="primary" style={{ borderRadius: '5px', marginTop: '5px', marginRight: '10px' }}>
                        Show Last Results
                    </Button>}
                    {playersContext.players.length < 5 && <Tooltip title='Available only when 5 players added'>
                        <QuestionCircleTwoTone  style={{marginTop: '10px', marginRight: '5px', fontSize: '24px'}} />
                    </Tooltip>}
                    <Button disabled={playersContext.players.length < 5} onClick={generateTeamsHandler} type="primary" style={{ borderRadius: '5px', marginTop: '5px' }}>
                            Run!
                    </Button>
                </div>

            </div>
        </div>
    )

}




function getTabsToShow(algos, generateTeamsHandler) {
    return algos && algos.map((algo) => {
        return {
            label: algo.displayName,
            key: algo.algoKey,
            children: <PlayersTable onGenerateClicked={generateTeamsHandler} style={{ height: '100px' }} algoKey={algo.algoKey} />
        }
    })
}


export default MainMenu;